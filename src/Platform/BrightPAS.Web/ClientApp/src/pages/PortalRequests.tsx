import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, errText, nzDateTime } from '../api';

const KIND: Record<string, string> = { ChangeAppointment: 'Change appointment', Cancel: 'Cancel', UpdateDetails: 'Update my details', Question: 'Question' };

/** Requests patients sent through the patient portal. The reply is shown to the patient in the portal. */
export default function PortalRequests() {
  const [all, setAll] = useState(false);
  const [rows, setRows] = useState<any[] | null>(null);
  const [reply, setReply] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const load = () => api<any[]>(`/api/portal/requests?all=${all}`).then(setRows).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, [all]); // eslint-disable-line react-hooks/exhaustive-deps
  const handle = async (id: string, status: 'Done' | 'Declined') => {
    setError(null);
    try { await api(`/api/portal/requests/${id}/handle`, { body: { status, response: reply[id] || null } }); load(); } catch (x) { setError(errText(x)); }
  };
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Patient portal requests</h1><p className="muted">Make the change in BrightPAS (booking, contact details…), then mark the request done with a short reply for the patient.</p></div>
      <label className="check"><input type="checkbox" checked={all} onChange={(e) => setAll(e.target.checked)} /> Show answered requests</label>
      {error && <p className="error">{error}</p>}
      <section className="card stack">
        {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No requests waiting.</p> : rows.map((r) => (
          <div key={r.id} className="card subtle stack">
            <div><strong>{KIND[r.kind] ?? r.kind}</strong> – <Link to={`/patients/${r.patientId}`}>{r.patientName}</Link> ({r.mrn}) · {nzDateTime(r.createdAt)}</div>
            <div className="mono-box">{r.message}</div>
            {r.status === 'Open' ? <div className="row"><input placeholder="Reply to the patient (shown in the portal)" value={reply[r.id] ?? ''} onChange={(e) => setReply({ ...reply, [r.id]: e.target.value })} />
              <button className="btn btn-primary" onClick={() => handle(r.id, 'Done')}>Done</button><button className="btn btn-light" onClick={() => handle(r.id, 'Declined')}>Decline</button></div>
              : <div className="small muted">{r.status} by {r.handledBy} {nzDateTime(r.handledAt)}{r.response && ` – "${r.response}"`}</div>}
          </div>))}
      </section>
    </div>
  );
}
