import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, errText, nzDateTime } from '../api';

/** Second-person check: identity corrections and patient merges waiting for approval. */
export default function IdentityQueue() {
  const [all, setAll] = useState(false);
  const [corr, setCorr] = useState<any[] | null>(null);
  const [merges, setMerges] = useState<any[] | null>(null);
  const [notes, setNotes] = useState<Record<string, string>>({});
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => {
    api<any[]>(`/api/patients/identity-corrections?all=${all}`).then(setCorr).catch((e) => setError(errText(e)));
    api<any[]>(`/api/patients/merges?all=${all}`).then(setMerges).catch((e) => setError(errText(e)));
  };
  useEffect(load, [all]); // eslint-disable-line react-hooks/exhaustive-deps
  const decide = async (kind: 'identity-corrections' | 'merges', id: string, approve: boolean) => {
    setError(null); setMsg(null);
    try { const r = await api(`/api/patients/${kind}/${id}/decide`, { body: { approve, note: notes[id] || null } }); setMsg(r?.result ? `Merged. Moved – ${r.result}` : approve ? 'Approved.' : 'Rejected.'); load(); }
    catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); }
  };
  const change = (c: any) => [c.newFamilyName && `family name → ${c.newFamilyName}`, c.newGivenNames && `given names → ${c.newGivenNames}`, c.newDateOfBirth && `DOB → ${c.newDateOfBirth}`,
    c.newSex && `sex → ${c.newSex}`, c.newNhi && `NHI → ${c.newNhi}`, c.clearNhi && 'remove NHI'].filter(Boolean).join('; ');
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Identity corrections &amp; merges</h1><p className="muted">A second person checks every change to a patient's identity. You cannot approve your own request.</p></div>
      <label className="check"><input type="checkbox" checked={all} onChange={(e) => setAll(e.target.checked)} /> Show decided requests too</label>
      {msg && <p className="ok-text">{msg}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      <section className="card stack"><h2>Corrections</h2>
        {!corr ? <p>Loading…</p> : corr.length === 0 ? <p className="muted">Nothing waiting.</p> : corr.map((c) => (
          <div key={c.id} className="card subtle stack">
            <div><Link to={`/patients/${c.patientId}`}>{c.mrn}</Link> – now: {c.before}</div>
            <div><strong>Change:</strong> {change(c)}</div>
            <div className="small">Reason: {c.reason}{c.evidence && ` · Evidence: ${c.evidence}`} · requested by {c.requestedBy} {nzDateTime(c.requestedAt)}</div>
            {c.status === 'Pending' ? <div className="row"><input placeholder="Note (required to reject)" value={notes[c.id] ?? ''} onChange={(e) => setNotes({ ...notes, [c.id]: e.target.value })} />
              <button className="btn btn-primary" onClick={() => decide('identity-corrections', c.id, true)}>Approve</button><button className="btn btn-light" onClick={() => decide('identity-corrections', c.id, false)}>Reject</button></div>
              : <div className="small muted">{c.status} by {c.decidedBy} {nzDateTime(c.decidedAt)} {c.decisionNote}</div>}
          </div>))}
      </section>
      <section className="card stack"><h2>Merges</h2>
        {!merges ? <p>Loading…</p> : merges.length === 0 ? <p className="muted">Nothing waiting.</p> : merges.map((m) => (
          <div key={m.id} className="card subtle stack">
            <div><strong>Keep:</strong> <Link to={`/patients/${m.survivorId}`}>{m.survivor}</Link></div>
            <div><strong>Merge in (duplicate):</strong> {m.duplicate}</div>
            <div className="small">Reason: {m.reason} · requested by {m.requestedBy} {nzDateTime(m.requestedAt)}</div>
            {!m.completedAt && m.status !== 'Rejected' ? <div className="row"><input placeholder="Note (required to reject)" value={notes[m.id] ?? ''} onChange={(e) => setNotes({ ...notes, [m.id]: e.target.value })} />
              <button className="btn btn-primary" onClick={() => decide('merges', m.id, true)}>{m.status === 'Approved' ? 'Finish merge' : 'Approve and merge'}</button>
              {m.status === 'Pending' && <button className="btn btn-light" onClick={() => decide('merges', m.id, false)}>Reject</button>}</div>
              : <div className="small muted">{m.status}{m.completedAt && ` – merged ${nzDateTime(m.completedAt)} by ${m.decidedBy}: ${m.result}`}</div>}
          </div>))}
      </section>
    </div>
  );
}
