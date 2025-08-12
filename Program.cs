using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// --------- 切換是否強制驗證（先保持 false，等前端改好再切 true）---------
const bool REQUIRE_AUTH = false;

var builder = WebApplication.CreateBuilder(args);

// ─── Render 埠號設定（本機預設 5030） ─────────────────────────────────────
var portEnv = Environment.GetEnvironmentVariable("PORT");
var port = string.IsNullOrWhiteSpace(portEnv) ? "5030" : portEnv;
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ─── CORS：允許 GitHub Pages 與本機（可再加白名單） ─────────────────────
var allowedOrigins = new[]
{
    "https://rachelchiang2002-lab.github.io",
    "http://localhost:5030",
    "http://127.0.0.1:5030"
};
builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ─── SQLite 路徑：Render 用 /tmp，本機用專案資料夾 ─────────────────────
string dbPath = string.IsNullOrWhiteSpace(portEnv)
    ? Path.Combine(Directory.GetCurrentDirectory(), "wfhdemo.db")
    : "/tmp/wfhdemo.db";
builder.Services.AddDbContext<AppDb>(opt => opt.UseSqlite($"Data Source={dbPath}"));

// ─── JWT 驗證設定 ────────────────────────────────────────────────────────
// Demo 用密鑰：可設環境變數 JWT_KEY 覆蓋
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") 
             ?? "dev-very-secret-key-please-change";
var keyBytes = Encoding.UTF8.GetBytes(jwtKey);
var signingKey = new SymmetricSecurityKey(keyBytes);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// ─── 極簡「使用者清單」：10 人內 Demo 用 ────────────────────────────────
// 密碼請用 BCrypt 雜湊；下方示範兩位，其他可依樣新增
var users = new List<SimpleUser>
{
    new SimpleUser { Username = "alice", Name = "Alice Chen",  Role = "user", 
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456") },
    new SimpleUser { Username = "boss",  Name = "Dept Head",   Role = "approver",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret") }
    // 再加：new SimpleUser { Username="john", Name="John", Role="user", PasswordHash=BCrypt.HashPassword("密碼") }
};

var app = builder.Build();
app.UseCors("AppCors");

// 啟動時若資料庫不存在就建立
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
}

// ─── Health 與首頁（匿名） ───────────────────────────────────────────────
app.MapGet("/", () => "WFH Api is running");
app.MapGet("/api/health", () => new { ok = true, time = DateTime.UtcNow });

// ─── 登入（匿名）：POST /api/login 取得 JWT ─────────────────────────────
app.MapPost("/api/login", (LoginDto dto) =>
{
    var u = users.FirstOrDefault(x => x.Username.Equals(dto.Username, StringComparison.OrdinalIgnoreCase));
    if (u is null || !BCrypt.Net.BCrypt.Verify(dto.Password ?? "", u.PasswordHash))
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, u.Username),
        new Claim(ClaimTypes.Name, u.Name ?? u.Username),
        new Claim(ClaimTypes.Role, u.Role ?? "user")
    };

    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddHours(8), // 有效 8 小時（可調成 1 天）
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
    );

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { token = jwt, name = u.Name, role = u.Role });
}).AllowAnonymous();

// ─── API 群組（可一鍵切換是否需要認證） ─────────────────────────────────
var api = app.MapGroup("/api");
if (REQUIRE_AUTH)
{
    api.RequireAuthorization(); // 切 true 後，除了上面標 AllowAnonymous 的，其餘都要帶 Bearer
}

// 建立申請單
api.MapPost("/applications", async (AppDb db, CreateAppDto dto) =>
{
    var row = new ApplicationRow
    {
        ApplicantEmail = "demo@local",
        ApplicantName  = "Demo User",
        Department     = "DemoDept",
        DatesJson      = System.Text.Json.JsonSerializer.Serialize(dto.Dates ?? Array.Empty<string>()),
        Type           = string.IsNullOrWhiteSpace(dto.Type) ? "regular" : dto.Type,
        Reason         = dto.Reason,
        Status         = "pending_section",
        SubmitTime     = DateTime.UtcNow,
        LastUpdateTime = DateTime.UtcNow
    };
    db.Applications.Add(row);
    await db.SaveChangesAsync();
    return Results.Ok(new { appId = row.AppId });
});

// 讀取申請單清單
api.MapGet("/applications", async (AppDb db) =>
{
    var list = await db.Applications
        .OrderByDescending(x => x.SubmitTime)
        .Take(200)
        .ToListAsync();
    return Results.Ok(list);
});

// 核准（pending_section -> pending_department -> approved）
api.MapPost("/approve", async (AppDb db, ActionDto dto) =>
{
    var appRow = await db.Applications.FindAsync(dto.AppId);
    if (appRow is null) return Results.NotFound(new { message = "找不到申請單" });

    if (appRow.Status == "pending_section")        appRow.Status = "pending_department";
    else if (appRow.Status == "pending_department") appRow.Status = "approved";
    else return Results.BadRequest(new { message = $"狀態 {appRow.Status} 不能核准" });

    appRow.LastUpdateTime = DateTime.UtcNow;

    var seq = await db.Approvals.CountAsync(x => x.AppId == appRow.AppId) + 1;
    db.Approvals.Add(new ApprovalRow {
        AppId = appRow.AppId,
        ActionSeq = seq,
        ActorEmail = "approver@local",
        ActorName  = "Approver",
        ActorRole  = "section_head",
        Action     = "approved",
        Comment    = dto.Comment,
        ActorIp    = null,
        ActionTime = DateTime.UtcNow
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { status = appRow.Status });
});

// 駁回
api.MapPost("/reject", async (AppDb db, ActionDto dto) =>
{
    var appRow = await db.Applications.FindAsync(dto.AppId);
    if (appRow is null) return Results.NotFound(new { message = "找不到申請單" });

    if (appRow.Status != "pending_section" && appRow.Status != "pending_department")
        return Results.BadRequest(new { message = $"狀態 {appRow.Status} 不能駁回" });

    appRow.Status = "rejected";
    appRow.LastUpdateTime = DateTime.UtcNow;

    var seq = await db.Approvals.CountAsync(x => x.AppId == appRow.AppId) + 1;
    db.Approvals.Add(new ApprovalRow {
        AppId = appRow.AppId,
        ActionSeq = seq,
        ActorEmail = "approver@local",
        ActorName  = "Approver",
        ActorRole  = "section_head",
        Action     = "rejected",
        Comment    = dto.Comment,
        ActorIp    = null,
        ActionTime = DateTime.UtcNow
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { status = appRow.Status });
});

// 查稽核軌跡
api.MapGet("/approvals", async (AppDb db, long appId) =>
{
    var list = await db.Approvals
        .Where(x => x.AppId == appId)
        .OrderBy(x => x.ActionSeq)
        .ToListAsync();
    return Results.Ok(list);
});

// 啟用驗證/授權中介軟體（放在 Map 之後也 OK）
app.UseAuthentication();
app.UseAuthorization();

app.Run();

// ─── DTO & 簡易使用者類別 ────────────────────────────────────────────────
public record CreateAppDto(string[]? Dates, string? Type, string? Reason);
public record ActionDto(long AppId, string? Comment);
public record LoginDto(string Username, string Password);
public class SimpleUser
{
    public string Username { get; set; } = default!;
    public string? Name    { get; set; }
    public string? Role    { get; set; }
    public string PasswordHash { get; set; } = default!;
}
