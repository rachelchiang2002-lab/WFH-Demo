const express = require('express');
const mongoose = require('mongoose');
const cors = require('cors');
const app = express();

app.use(cors());
app.use(express.json());

// 連接到 MongoDB (使用 Render 提供的環境變數)
mongoose.connect(process.env.MONGODB_URI || 'mongodb://localhost/wfh', {
  useNewUrlParser: true,
  useUnifiedTopology: true
});

// 申請模型
const ApplicationSchema = new mongoose.Schema({
  applicant: String,
  applicantName: String,
  department: String,
  dates: [String],
  type: String,
  reason: String,
  status: String,
  submitTime: String,
  approvals: [{ approver: String, approverName: String, role: String, time: String, action: String, reason: String }]
});
const Application = mongoose.model('Application', ApplicationSchema);

// API 端點
app.get('/api/applications', async (req, res) => {
  const applications = await Application.find();
  res.json(applications);
});

app.post('/api/applications', async (req, res) => {
  const application = new Application(req.body);
  await application.save();
  res.json({ appId: application._id });
});

app.patch('/api/applications/:id', async (req, res) => {
  const application = await Application.findByIdAndUpdate(req.params.id, req.body, { new: true });
  res.json(application);
});

const PORT = process.env.PORT || 3000;
app.listen(PORT, () => {
  console.log(`Server running on port ${PORT}`);
});
