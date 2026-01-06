-- Create error_logs table for developer debugging
CREATE TABLE error_logs (
  id uuid DEFAULT gen_random_uuid() PRIMARY KEY,
  session_id uuid NOT NULL,
  error_type text NOT NULL,
  error_message text NOT NULL,
  stack_trace text,
  app_version text,
  platform text,
  created_at timestamptz DEFAULT now()
);

-- Enable RLS
ALTER TABLE error_logs ENABLE ROW LEVEL SECURITY;

-- Allow anonymous inserts for error logging
CREATE POLICY "Allow anonymous inserts" ON error_logs
  FOR INSERT 
  WITH CHECK (true);

-- Indexes for performance
CREATE INDEX idx_error_logs_session_id ON error_logs(session_id);
CREATE INDEX idx_error_logs_created_at ON error_logs(created_at);
CREATE INDEX idx_error_logs_error_type ON error_logs(error_type);