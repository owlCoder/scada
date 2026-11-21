namespace Infrastructure.Persistence.Postgres;

/// <summary>
/// Copies rows from the pre-EF schema (<c>deploy/postgres/init.sql</c>: public.security_events, public.telemetry_samples,
/// public.incidents) into the new tables. Each table is copied only when it exists, and the legacy tables are never
/// altered or dropped, so a rollback to the old build still finds its data.
/// </summary>
public static class LegacyDataImport
{
    public const string Source = "legacy-import";

    public const string Sql = """
        DO $legacy$
        BEGIN
            IF to_regclass('public.security_events') IS NOT NULL THEN
                INSERT INTO scada.events (id, timestamp, category, severity, source, message, actor)
                SELECT gen_random_uuid(), e.occurred_at, left(e.category, 64), 'Information', 'legacy-import', e.message, left(e.actor, 128)
                FROM public.security_events e
                ORDER BY e.id;
            END IF;

            IF to_regclass('public.telemetry_samples') IS NOT NULL THEN
                INSERT INTO scada.telemetry (sequence, timestamp, simulation_time, tag_id, value, quality, hash)
                SELECT t.id, t.recorded_at, INTERVAL '0', 'LT-101', t.tank_level, 'Legacy', 'legacy-import'
                FROM public.telemetry_samples t
                ORDER BY t.id;

                INSERT INTO scada.telemetry (sequence, timestamp, simulation_time, tag_id, value, quality, hash)
                SELECT t.id, t.recorded_at, INTERVAL '0', 'P-101.running', CASE WHEN t.pump_running THEN 1 ELSE 0 END, 'Legacy', 'legacy-import'
                FROM public.telemetry_samples t
                ORDER BY t.id;
            END IF;

            IF to_regclass('public.incidents') IS NOT NULL THEN
                -- Legacy status: 0 Open, 1 Acknowledged, 2 Resolved. New enum: 0 Open, 1 Acknowledged, 3 Resolved.
                -- Severity 1 = Medium, priority 1 = Normal.
                INSERT INTO scada.incidents (id, title, description, category, severity, priority, source, status, created_at, updated_at,
                    acknowledged_by, acknowledged_at, resolved_by, resolved_at, device_ids, device_keys, correlation_key, evidence, version)
                SELECT i.id, left(i.description, 256), i.description,
                    CASE lower(i.category)
                        WHEN 'processsafety' THEN 'ProcessSafety'
                        WHEN 'safety' THEN 'ProcessSafety'
                        WHEN 'communication' THEN 'Communication'
                        WHEN 'equipment' THEN 'Equipment'
                        WHEN 'cybersecurity' THEN 'Cybersecurity'
                        WHEN 'security' THEN 'Cybersecurity'
                        WHEN 'authentication' THEN 'Cybersecurity'
                        WHEN 'authorization' THEN 'Cybersecurity'
                        ELSE 'Operational'
                    END,
                    1, 1, left('legacy:' || i.actor, 128),
                    CASE i.status WHEN 0 THEN 0 WHEN 1 THEN 1 ELSE 3 END,
                    i.created_at, COALESCE(i.updated_at, i.created_at),
                    CASE WHEN i.status = 1 THEN left(i.updated_by, 128) END, CASE WHEN i.status = 1 THEN i.updated_at END,
                    CASE WHEN i.status = 2 THEN left(i.updated_by, 128) END, CASE WHEN i.status = 2 THEN i.updated_at END,
                    '{}', '{}', 'legacy:' || i.id::text, '[]', 1
                FROM public.incidents i
                ORDER BY i.created_at, i.id
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO scada.incident_timeline (id, incident_id, timestamp, actor, kind, description, to_status)
                SELECT gen_random_uuid(), i.id, i.created_at, left(i.actor, 128), 'Created', 'Imported from the legacy incidents table',
                    CASE i.status WHEN 0 THEN 'Open' WHEN 1 THEN 'Acknowledged' ELSE 'Resolved' END
                FROM public.incidents i
                WHERE EXISTS (SELECT 1 FROM scada.incidents n WHERE n.id = i.id AND n.correlation_key = 'legacy:' || i.id::text);
            END IF;
        END
        $legacy$;
        """;
}
