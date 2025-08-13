const express = require('express');
const { Pool } = require('pg');
const cors = require('cors');
const app = express();

const pool = new Pool({
  connectionString: process.env.DATABASE_URL,
});

app.use(cors());
app.use(express.json());

app.get('/api/applications', async (req, res) => {
  try {
    const result = await pool.query('SELECT * FROM applications');
    res.json(result.rows);
  } catch (err) {
    console.error(err);
    res.status(500).json({ error: 'Database error' });
  }
});

const PORT = process.env.PORT || 10000;
app.listen(PORT, () => {
  console.log(`Server running on port ${PORT}`);
});
