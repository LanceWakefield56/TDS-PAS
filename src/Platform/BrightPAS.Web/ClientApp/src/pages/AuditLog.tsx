import { useEffect, useState } from 'react';
import { api } from '../api';

interface AuditRow { sequence: number; occurredAt: string; userId: string; action: string; entityType: string; entityId: string; patientId: string | null; reason: string | null; workstation: string | null }
interface Integrity { intact: boolean; records: number; firstBrokenSequence: number | null; checkedAt: string }

export default function AuditLog() {
  const [rows, setRows] = useState<AuditRow[]>([]);
  const [filter, setFilter] = useState({ user: '', patient: '', action: '' });
  const [integrity, setIntegrity] = useState<Integrity | null>(null);
  const [checking, setChecking] = useState(false);

  const load = async (e?: React.FormEvent) => {
    e?.preventDefault();
    const p = new URLSearchParams(Object.entries(filter).filter(([, v]) => v.trim()) as [string, string][]);
    setRows(await api<AuditRow[]>(`/api/audit?${p}`));
  };
  useEffect(() => { load(); }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const verify = async () => { setChecking(true); try { setIntegrity(await api<Integrity>('/api/audit/verify')); } finally { setChecking(false); } };

  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div><h1>Audit log</h1><p className="muted">Every sign-in, record view and change. Records cannot be altered or deleted.</p></div>
        <button className="btn btn-primary" onClick={verify} disabled={checking}>{checking ? 'Checking…' : 'Check audit log integrity'}</button>
      </div>
      {integrity && (integrity.intact
        ? <p className="notice" role="status">Audit log intact: all {integrity.records.toLocaleString()} records verified ({new Date(integrity.checkedAt).toLocaleString('en-NZ')}).</p>
        : <p className="error" role="alert"><strong>WARNING:</strong> the audit log has been tampered with at record {integrity.firstBrokenSequence}. Report this to the Privacy Officer and IT immediately.</p>)}
      <form className="card search" onSubmit={load}>
        <label>User<input value={filter.user} onChange={(e) => setFilter({ ...filter, user: e.target.value })} placeholder="username" /></label>
        <label>Patient id<input value={filter.patient} onChange={(e) => setFilter({ ...filter, patient: e.target.value })} /></label>
        <label>Action<input value={filter.action} onChange={(e) => setFilter({ ...filter, action: e.target.value })} placeholder="e.g. View, Login, BreakGlass" /></label>
        <button className="btn btn-primary">Filter</button>
      </form>
      <section className="card">
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Table (scrolls sideways)"><table className="results compact">
          <thead><tr><th>#</th><th>When</th><th>User</th><th>Action</th><th>Record</th><th>Patient</th><th>Details</th><th>From</th></tr></thead>
          <tbody>{rows.map((r) => (
            <tr key={r.sequence}><td>{r.sequence}</td><td>{new Date(r.occurredAt).toLocaleString('en-NZ')}</td><td>{r.userId}</td><td><strong>{r.action}</strong></td>
              <td className="small">{r.entityType}</td><td className="small">{r.patientId ? r.patientId.slice(0, 8) + '…' : ''}</td><td className="small">{r.reason}</td><td className="small">{r.workstation}</td></tr>))}
          </tbody>
        </table></div>
      </section>
    </div>
  );
}
