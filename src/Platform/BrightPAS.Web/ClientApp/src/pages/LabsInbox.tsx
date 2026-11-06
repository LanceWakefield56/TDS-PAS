import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router';
import { api, errText, nzDateTime, Perm } from '../api';
import { useSession } from '../App';

export interface LabRow {
  id: string; patientId: string | null; patientName: string; nhi: string | null; mrn: string | null; laboratory: string; testName: string; requestedBy: string | null;
  collectedAt: string | null; reportedAt: string | null; receivedAt: string; resultStatus: string; isAbnormal: boolean; matchStatus: string; matchNote: string | null;
  acknowledgedAt: string | null; acknowledgedBy: string | null;
}

export default function LabsInbox() {
  const { can, writable } = useSession();
  const navigate = useNavigate();
  const [view, setView] = useState('unacknowledged');
  const [rows, setRows] = useState<LabRow[] | null>(null);
  const [mine, setMine] = useState('');
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setError(null);
    try {
      setRows(view === 'unmatched' ? await api<LabRow[]>('/api/labs/unmatched')
        : await api<LabRow[]>(`/api/labs?view=${view}${mine ? `&requestedBy=${encodeURIComponent(mine)}` : ''}`));
    } catch (e) { setError(errText(e)); }
  };
  useEffect(() => { load(); }, [view]); // eslint-disable-line react-hooks/exhaustive-deps

  const tabs: [string, string][] = [['unacknowledged', 'To acknowledge'], ['abnormal', 'Abnormal'], ['all', 'All recent']];
  if (can(Perm.LabsMatch)) tabs.push(['unmatched', 'Unmatched']);
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Lab results</h1><p className="muted">Results received from laboratories. Acknowledge each result once you have reviewed it.</p></div>
      {!writable('labs') && <p className="warn-box">Lab results are read-only because of a licence problem. You can view results but not acknowledge them.</p>}
      <section className="card">
        <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={view === k} className={view === k ? 'on' : ''} onClick={() => setView(k)}>{t}</button>)}</div>
        {view !== 'unmatched' && (
          <form className="row mb" onSubmit={(e) => { e.preventDefault(); load(); }}>
            <label>Requested by (contains)<input value={mine} onChange={(e) => setMine(e.target.value)} placeholder="e.g. Smith" /></label>
            <button className="btn btn-light">Filter</button>
          </form>
        )}
        {error && <p className="error">{error}</p>}
        {rows && rows.length === 0 && <p className="muted">Nothing here.</p>}
        {rows && rows.length > 0 && (
          <table className="results">
            <thead><tr><th>Received</th><th>Patient</th><th>Test</th><th>Requested by</th><th>Status</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id} tabIndex={0} className={r.isAbnormal ? 'abnormal' : ''} onClick={() => navigate(`/labs/${r.id}`)} onKeyDown={(e) => e.key === 'Enter' && navigate(`/labs/${r.id}`)}>
                <td data-label="Received" className="time">{nzDateTime(r.receivedAt)}</td>
                <td data-label="Patient"><strong>{r.patientName}</strong><div className="muted small">{r.nhi ?? '—'} · {r.mrn ?? '—'}</div>{r.matchNote && <div className="small warn-text">{r.matchNote}</div>}</td>
                <td data-label="Test">{r.testName}<div className="muted small">{r.laboratory}</div></td>
                <td data-label="Requested by">{r.requestedBy ?? '—'}</td>
                <td data-label="Status">
                  {r.isAbnormal && <span className="pill red">Abnormal</span>} <span className="pill">{r.resultStatus}</span>{' '}
                  {r.patientId === null ? <span className="pill amber">Unmatched</span> : r.acknowledgedAt ? <span className="pill green">Acknowledged</span> : <span className="pill blue">New</span>}
                </td>
              </tr>
            ))}</tbody>
          </table>
        )}
      </section>
    </div>
  );
}
