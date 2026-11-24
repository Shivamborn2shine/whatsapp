-- ============================================================================
-- Scan Drop — Multi-Shop Control Plane Schema (Supabase Postgres)
-- Project: yagbslkhttuogbihegwe
--
-- Architecture:
--   - Customer uploaded files NEVER touch Supabase or any cloud storage.
--   - Supabase stores ONLY shop metadata, pairing codes, hashed PC device
--     secrets, live tunnel URLs, subscription status, and superadmin audit logs.
--
-- Setup:
--   1. Run this entire script once in Supabase SQL Editor.
--   2. Create your founder account in Supabase -> Authentication -> Users
--      (or sign up once), then run the grant query at the bottom with your email
--      to add yourself to public.superadmins.
--   3. Add these environment variables in Vercel -> Project Settings -> Env Vars:
--        SUPABASE_URL=https://yagbslkhttuogbihegwe.supabase.co
--        SUPABASE_PUBLISHABLE_KEY=<your publishable / anon key>
--        SUPABASE_SECRET_KEY=<your secret / service_role key>
--        SUPERADMIN_EMAILS=<your-founder-email@example.com>
-- ============================================================================

create schema if not exists private;

-- 1. Superadmins allowlist (authorization table — never use user_metadata!)
create table if not exists public.superadmins (
  user_id uuid primary key references auth.users (id) on delete cascade,
  email text not null unique,
  created_at timestamptz not null default now()
);

-- 2. Shops registry (1 row per shopkeeper / counter)
create table if not exists public.shops (
  id bigint generated always as identity primary key,
  shop_uid text not null unique,
  slug text not null unique check (slug ~ '^[a-z0-9][a-z0-9-]{1,48}$'),
  name text not null,
  tagline text not null default 'Scan. Send. Done.',
  owner_name text not null default '',
  owner_email text not null default '',
  owner_phone text not null default '',
  plan_id text not null default 'plan_pro_monthly',
  subscription_status text not null default 'active'
    check (subscription_status in ('trial', 'active', 'paused', 'suspended', 'revoked')),
  max_devices integer not null default 1 check (max_devices >= 1 and max_devices <= 100),
  pairing_code text not null unique,
  pairing_code_used_at timestamptz,
  device_secret_hash text,
  tunnel_url text,
  tunnel_updated_at timestamptz,
  expiry_date timestamptz not null default (now() + interval '30 days'),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

-- 3. Paired Windows PC devices per shop
create table if not exists public.devices (
  id bigint generated always as identity primary key,
  device_uid text not null unique,
  shop_id bigint not null references public.shops (id) on delete cascade,
  device_name text not null,
  machine_fingerprint text not null default '',
  device_secret_hash text not null,
  windows_version text not null default '',
  app_version text not null default '',
  status text not null default 'active' check (status in ('active', 'revoked')),
  tunnel_url text,
  last_heartbeat_at timestamptz not null default now(),
  created_at timestamptz not null default now()
);

-- 4. Control-plane audit log
create table if not exists public.audit_events (
  id bigint generated always as identity primary key,
  shop_id bigint references public.shops (id) on delete set null,
  actor_email text not null default 'system',
  event_type text not null,
  description text not null,
  created_at timestamptz not null default now()
);

-- ============================================================================
-- Indexes (FK columns + hot lookups)
-- ============================================================================
create index if not exists shops_status_idx on public.shops (subscription_status);
create index if not exists devices_shop_id_idx on public.devices (shop_id);
create index if not exists devices_secret_hash_idx on public.devices (device_secret_hash);
create index if not exists devices_shop_fingerprint_idx on public.devices (shop_id, machine_fingerprint);
create index if not exists audit_events_shop_id_idx on public.audit_events (shop_id);
create index if not exists audit_events_created_at_idx on public.audit_events (created_at desc);

-- Automatically maintain updated_at on shops
create or replace function private.touch_updated_at()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists shops_touch_updated_at on public.shops;
create trigger shops_touch_updated_at
  before update on public.shops
  for each row
  execute function private.touch_updated_at();

-- ============================================================================
-- Private RLS Helper (non-exposed schema, checks auth.uid() explicitly)
-- ============================================================================
create or replace function private.is_superadmin()
returns boolean
language sql
security definer
set search_path = ''
as $$
  select exists (
    select 1
    from public.superadmins
    where user_id = (select auth.uid())
  );
$$;

revoke execute on function private.is_superadmin() from public, anon;
grant execute on function private.is_superadmin() to authenticated, service_role;

-- ============================================================================
-- Row-Level Security & Principle of Least Privilege
-- ============================================================================
alter table public.superadmins enable row level security;
alter table public.shops enable row level security;
alter table public.devices enable row level security;
alter table public.audit_events enable row level security;

-- Block all direct anonymous Data API access (public shop lookups go through
-- Vercel /api/shop which returns ONLY non-sensitive public fields).
revoke all on public.superadmins, public.shops, public.devices, public.audit_events from anon;

-- Ensure server-side service_role has full access
grant usage on schema public to service_role, authenticated;
grant all on public.superadmins, public.shops, public.devices, public.audit_events to service_role;
grant usage, select on all sequences in schema public to service_role;

-- Allow authenticated users only when verified as superadmins via RLS
grant select on public.superadmins to authenticated;
grant select, insert, update, delete on public.shops, public.devices, public.audit_events to authenticated;
grant usage, select on all sequences in schema public to authenticated;

-- Superadmins table policies
drop policy if exists superadmins_self_read on public.superadmins;
create policy superadmins_self_read on public.superadmins
  for select
  to authenticated
  using ((select auth.uid()) = user_id);

-- Shops table policies (superadmin-only direct access)
drop policy if exists shops_superadmin_select on public.shops;
create policy shops_superadmin_select on public.shops
  for select
  to authenticated
  using ((select private.is_superadmin()));

drop policy if exists shops_superadmin_insert on public.shops;
create policy shops_superadmin_insert on public.shops
  for insert
  to authenticated
  with check ((select private.is_superadmin()));

drop policy if exists shops_superadmin_update on public.shops;
create policy shops_superadmin_update on public.shops
  for update
  to authenticated
  using ((select private.is_superadmin()))
  with check ((select private.is_superadmin()));

drop policy if exists shops_superadmin_delete on public.shops;
create policy shops_superadmin_delete on public.shops
  for delete
  to authenticated
  using ((select private.is_superadmin()));

-- Devices table policies
drop policy if exists devices_superadmin_select on public.devices;
create policy devices_superadmin_select on public.devices
  for select
  to authenticated
  using ((select private.is_superadmin()));

drop policy if exists devices_superadmin_insert on public.devices;
create policy devices_superadmin_insert on public.devices
  for insert
  to authenticated
  with check ((select private.is_superadmin()));

drop policy if exists devices_superadmin_update on public.devices;
create policy devices_superadmin_update on public.devices
  for update
  to authenticated
  using ((select private.is_superadmin()))
  with check ((select private.is_superadmin()));

drop policy if exists devices_superadmin_delete on public.devices;
create policy devices_superadmin_delete on public.devices
  for delete
  to authenticated
  using ((select private.is_superadmin()));

-- Audit events policies
drop policy if exists audit_events_superadmin_select on public.audit_events;
create policy audit_events_superadmin_select on public.audit_events
  for select
  to authenticated
  using ((select private.is_superadmin()));

drop policy if exists audit_events_superadmin_insert on public.audit_events;
create policy audit_events_superadmin_insert on public.audit_events
  for insert
  to authenticated
  with check ((select private.is_superadmin()));

-- ============================================================================
-- Seed existing host shop (quickprint) so current PC keeps working smoothly
-- ============================================================================
insert into public.shops (
  shop_uid, slug, name, tagline, owner_name, plan_id,
  subscription_status, max_devices, pairing_code
) values (
  'shop_quickprint_01',
  'quickprint',
  'QuickPrint Xerox & Stationery',
  'Scan. Send. Done.',
  'Primary Host',
  'plan_pro_monthly',
  'active',
  1,
  'SD-PAIR-7890'
)
on conflict (slug) do nothing;

-- ============================================================================
-- Optional: Promote an existing Supabase Auth user to Superadmin
-- (Replace with your founder email after creating the user in Auth -> Users)
-- ============================================================================
-- insert into public.superadmins (user_id, email)
-- select id, email from auth.users where lower(email) = lower('you@example.com')
-- on conflict (user_id) do nothing;
