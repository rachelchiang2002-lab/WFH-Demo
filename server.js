const express = require('express');
const { Pool } = require('pg');
const cors = require('cors');
const app = express();

const pool = new Pool({
  connectionString: process.env.DATABASE_URL,
});

// 初始化表（僅在表不存在時執行）
(async () => {
  try {
    await pool.query(`
      CREATE TABLE IF NOT EXISTS applications (
        id SERIAL PRIMARY KEY,
        applicant VARCHAR(255),
        applicantName VARCHAR(255),
        department VARCHAR(255),
        dates JSONB,
        type VARCHAR(255),
        reason TEXT,
        status VARCHAR(255),
        submitTime TIMESTAMP,
        approvals JSONB
      )
    `);
    console.log('Table "applications" created or already exists');
  } catch (err) {
    console.error('Error creating table:', err);
  }
})();

app.use(cors());
app.use(express.json());

app.get('/api/applications', async (req, res) => {
  try {
    const result = await pool.query('SELECT * FROM applications');
    res.json(result.rows);
  } catch (err) {
    console.error(err);
    if (err.code === '42P01') { // 表不存在錯誤（應已處理）
      res.json([]);
    } else {
      res.status(500).json({ error: 'Database error' });
    }
  }
});

const PORT = process.env.PORT || 10000;
app.listen(PORT, () => {
  console.log(`Server running on port ${PORT}`);
});
