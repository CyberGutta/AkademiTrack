-- Complete Supabase Analytics Setup
-- Run this in Supabase SQL Editor

-- Drop existing tables if they exist
DROP TABLE IF EXISTS public.error_logs;
DROP TABLE IF EXISTS public.events;
DROP TABLE IF EXISTS public.sessions;

-- Create sessions table
CREATE TABLE public.sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id TEXT NOT NULL,
    platform TEXT NOT NULL,
    app_version TEXT NOT NULL,
    started_at TIMESTAMPTZ DEFAULT NOW(),
    last_action TEXT NOT NULL DEFAULT 'app_opened',
    last_action_at TIMESTAMPTZ DEFAULT NOW(),
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Create error_logs table (removed events table)
CREATE TABLE public.error_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id UUID REFERENCES public.sessions(id) ON DELETE CASCADE,
    user_id TEXT NOT NULL,
    error_type TEXT NOT NULL,
    error_message TEXT NOT NULL,
    stack_trace TEXT,
    app_version TEXT,
    platform TEXT,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Enable Row Level Security (RLS)
ALTER TABLE public.sessions ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.error_logs ENABLE ROW LEVEL SECURITY;

-- Create policies for anonymous access

-- Sessions policies
CREATE POLICY "Allow anonymous inserts on sessions" ON public.sessions
    FOR INSERT TO anon WITH CHECK (true);

CREATE POLICY "Allow anonymous selects on sessions" ON public.sessions
    FOR SELECT TO anon USING (true);

CREATE POLICY "Allow anonymous updates on sessions" ON public.sessions
    FOR UPDATE TO anon USING (true);

-- Error logs policies
CREATE POLICY "Allow anonymous inserts on error_logs" ON public.error_logs
    FOR INSERT TO anon WITH CHECK (true);

CREATE POLICY "Allow anonymous selects on error_logs" ON public.error_logs
    FOR SELECT TO anon USING (true);

-- Create indexes for better performance
CREATE INDEX idx_sessions_user_id ON public.sessions(user_id);
CREATE INDEX idx_sessions_started_at ON public.sessions(started_at);
CREATE INDEX idx_sessions_last_action ON public.sessions(last_action);
CREATE INDEX idx_sessions_last_action_at ON public.sessions(last_action_at);
CREATE INDEX idx_error_logs_session_id ON public.error_logs(session_id);
CREATE INDEX idx_error_logs_user_id ON public.error_logs(user_id);
CREATE INDEX idx_error_logs_created_at ON public.error_logs(created_at);

-- Grant permissions to anon role
GRANT INSERT, SELECT, UPDATE ON public.sessions TO anon;
GRANT INSERT, SELECT ON public.error_logs TO anon;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA public TO anon;

-- Migration script for existing databases
-- Run this if you already have the sessions table and want to add the new columns:

/*
-- Add new columns to existing sessions table
ALTER TABLE public.sessions 
ADD COLUMN IF NOT EXISTS last_action TEXT DEFAULT 'app_opened',
ADD COLUMN IF NOT EXISTS last_action_at TIMESTAMPTZ DEFAULT NOW();

-- Remove old column if it exists
ALTER TABLE public.sessions DROP COLUMN IF EXISTS last_automation_start;

-- Update existing records to have default values
UPDATE public.sessions 
SET last_action = 'app_opened', last_action_at = started_at 
WHERE last_action IS NULL;

-- Make last_action NOT NULL after setting defaults
ALTER TABLE public.sessions ALTER COLUMN last_action SET NOT NULL;

-- Create new indexes
CREATE INDEX IF NOT EXISTS idx_sessions_last_action ON public.sessions(last_action);
CREATE INDEX IF NOT EXISTS idx_sessions_last_action_at ON public.sessions(last_action_at);

-- Drop old index if it exists
DROP INDEX IF EXISTS idx_sessions_last_automation;
*/

-- ===== ANALYTICS QUERIES =====
-- Use these queries in Supabase to analyze your user data

-- 1. TOTAL UNIQUE USERS (all time)
/*
SELECT COUNT(DISTINCT user_id) as total_users
FROM sessions;
*/

-- 2. ACTIVE USERS (last 15 minutes)
/*
SELECT COUNT(DISTINCT user_id) as active_users
FROM sessions 
WHERE last_action_at > NOW() - INTERVAL '15 minutes'
AND last_action != 'deleted';
*/

-- 3. USERS WITH AUTOMATION ACTIVE (last 10 minutes)
/*
SELECT COUNT(DISTINCT user_id) as automation_active_users
FROM sessions 
WHERE last_action IN ('automation_active', 'automation_started')
AND last_action_at > NOW() - INTERVAL '10 minutes';
*/

-- 4. USER ACTIVITY BREAKDOWN (last 24 hours)
/*
SELECT 
    last_action,
    COUNT(DISTINCT user_id) as user_count,
    MAX(last_action_at) as most_recent
FROM sessions 
WHERE last_action_at > NOW() - INTERVAL '24 hours'
GROUP BY last_action
ORDER BY user_count DESC;
*/

-- 5. PLATFORM DISTRIBUTION (active users last hour)
/*
SELECT 
    platform,
    COUNT(DISTINCT user_id) as users
FROM sessions 
WHERE last_action_at > NOW() - INTERVAL '1 hour'
AND last_action != 'deleted'
GROUP BY platform
ORDER BY users DESC;
*/

-- 6. HOURLY ACTIVITY (last 24 hours)
/*
SELECT 
    DATE_TRUNC('hour', last_action_at) as hour,
    COUNT(DISTINCT user_id) as active_users,
    COUNT(DISTINCT CASE WHEN last_action IN ('automation_active', 'automation_started') THEN user_id END) as automation_users
FROM sessions 
WHERE last_action_at > NOW() - INTERVAL '24 hours'
AND last_action != 'deleted'
GROUP BY hour
ORDER BY hour DESC;
*/

-- 7. USER SESSION DETAILS (for debugging)
/*
SELECT 
    user_id,
    platform,
    app_version,
    last_action,
    last_action_at,
    started_at
FROM sessions 
ORDER BY last_action_at DESC
LIMIT 20;
*/

-- 8. DELETED USERS (users who deleted their data)
/*
SELECT 
    user_id,
    platform,
    app_version,
    last_action_at as deleted_at,
    started_at as first_seen
FROM sessions 
WHERE last_action = 'deleted'
ORDER BY last_action_at DESC;
*/

-- 9. USER RETENTION (active vs deleted)
/*
SELECT 
    COUNT(DISTINCT CASE WHEN last_action != 'deleted' THEN user_id END) as active_users,
    COUNT(DISTINCT CASE WHEN last_action = 'deleted' THEN user_id END) as deleted_users,
    COUNT(DISTINCT user_id) as total_users,
    ROUND(100.0 * COUNT(DISTINCT CASE WHEN last_action = 'deleted' THEN user_id END) / COUNT(DISTINCT user_id), 2) as deletion_rate_percent
FROM sessions;
*/

-- 10. DAILY NEW USERS (last 30 days)
/*
SELECT 
    DATE(started_at) as date,
    COUNT(DISTINCT user_id) as new_users
FROM sessions 
WHERE started_at > NOW() - INTERVAL '30 days'
GROUP BY DATE(started_at)
ORDER BY date DESC;
*/
