app.post('/api/applications', async (req, res) => {
  try {
    const { applicant, applicantName, department, dates, type, reason, status, submitTime, approvals } = req.body;
    const result = await pool.query(
      `INSERT INTO applications (applicant, applicantName, department, dates, type, reason, status, submitTime, approvals) 
       VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9) 
       RETURNING *`,
      [applicant, applicantName, department, dates, type, reason, status, submitTime, approvals]
    );
    res.json(result.rows[0]);
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: 'Database error' });
  }
});
