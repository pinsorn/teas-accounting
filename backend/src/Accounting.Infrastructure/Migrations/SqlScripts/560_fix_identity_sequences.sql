-- ============================================================
-- 560_fix_identity_sequences.sql  —  2026-06-16
-- ============================================================
-- The demo/company seeds (120/400/440) INSERT master.companies (and HQ branches)
-- with EXPLICIT ids (1,2,3). An explicit INSERT into an IDENTITY column does NOT
-- advance the column's sequence, so the FIRST app-created row collides:
-- POST /companies (super-admin onboarding / company create) 500s with
-- "company_id=N already exists" until the sequence is advanced past the seeded max.
--
-- Fix: setval each IDENTITY sequence to the current MAX (is_called=true → next = MAX+1).
-- Idempotent + guarded (skips a table whose column is not an IDENTITY/serial, where
-- pg_get_serial_sequence returns NULL). Runs last (560) so all company/branch seeds exist.
-- ============================================================

DO $$
DECLARE
    seqname text;
BEGIN
    seqname := pg_get_serial_sequence('master.companies', 'company_id');
    IF seqname IS NOT NULL THEN
        PERFORM setval(seqname,
            GREATEST((SELECT COALESCE(MAX(company_id), 0) FROM master.companies), 1), true);
    END IF;

    seqname := pg_get_serial_sequence('master.branches', 'branch_id');
    IF seqname IS NOT NULL THEN
        PERFORM setval(seqname,
            GREATEST((SELECT COALESCE(MAX(branch_id), 0) FROM master.branches), 1), true);
    END IF;
END $$;
