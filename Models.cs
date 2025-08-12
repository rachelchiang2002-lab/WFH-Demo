using System.ComponentModel.DataAnnotations;

public class ApplicationRow
{
    [Key]                           // 主鍵
    public long AppId { get; set; } // 申請單ID（自動遞增）

    public string ApplicantEmail { get; set; } = "demo@local";
    public string ApplicantName  { get; set; } = "Demo User";
    public string Department     { get; set; } = "DemoDept";
    public string DatesJson      { get; set; } = "[]";       // 例如 ["2025-08-15","2025-08-20"]
    public string Type           { get; set; } = "regular";  // regular | urgent
    public string? Reason        { get; set; }
    public string Status         { get; set; } = "pending_section";
    public DateTime SubmitTime   { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdateTime { get; set; } = DateTime.UtcNow;
}

public class ApprovalRow
{
    [Key]                           // 主鍵
    public long Id { get; set; }

    public long AppId { get; set; }
    public int  ActionSeq { get; set; }                 // 1,2,3…
    public string ActorEmail { get; set; } = "approver@local";
    public string ActorName  { get; set; } = "Approver";
    public string ActorRole  { get; set; } = "section_head"; // 角色
    public string Action     { get; set; } = "approved";     // approved | rejected
    public string? Comment   { get; set; }
    public string? ActorIp   { get; set; }
    public DateTime ActionTime { get; set; } = DateTime.UtcNow;
}
