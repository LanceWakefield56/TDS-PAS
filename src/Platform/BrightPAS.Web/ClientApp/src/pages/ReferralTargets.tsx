import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { api, errText, nzDateTime } from '../api';

/** Administrator set-up step: waiting-time targets for each priority (Referrals & waitlist). */
export default function ReferralTargets() {
  const navigate = useNavigate();
  const [f, setF] = useState<{ urgentDays: number; semiUrgentDays: number; routineDays: number; updatedAt: string | null; updatedBy: string | null } | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api<any>('/api/referrals/settings').then(setF).catch((e) => setErrors([errText(e)])); }, []);
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setErrors([]);
    try { await api('/api/referrals/settings', { method: 'PUT', body: { urgentDays: f!.urgentDays, semiUrgentDays: f!.semiUrgentDays, routineDays: f!.routineDays } }); navigate('/admin/modules/referrals'); }
    catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  if (!f) return errors.length ? <p className="error">{errors[0]}</p> : <p>Loading…</p>;
  const num = (k: 'urgentDays' | 'semiUrgentDays' | 'routineDays') => (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: Number(e.target.value) });
  return (
    <div className="stack-lg">
      <div className="page-head">
        <p className="small"><Link to="/admin/modules/referrals">← Set up: Referrals &amp; waitlist</Link></p>
        <h1>Waiting-time targets</h1>
        <p className="muted">Days from the decision to treat. The waitlist shows each patient's target date and flags anyone past it. Changing a target affects new entries and priority changes.</p>
      </div>
      <form className="card stack" onSubmit={save}>
        <div className="form-grid">
          <label>Urgent (days)<input type="number" min={1} max={365} value={f.urgentDays} onChange={num('urgentDays')} /></label>
          <label>Semi-urgent (days)<input type="number" min={1} max={365} value={f.semiUrgentDays} onChange={num('semiUrgentDays')} /></label>
          <label>Routine (days)<input type="number" min={1} max={730} value={f.routineDays} onChange={num('routineDays')} /></label>
        </div>
        <p className="hint">Health NZ's planned-care target is treatment within 4 months (about 120 days) for publicly funded work.</p>
        {f.updatedAt && <p className="hint">Last confirmed by {f.updatedBy}, {nzDateTime(f.updatedAt)}.</p>}
        {errors.length > 0 && <div className="error" role="alert">{errors.map((x) => <div key={x}>{x}</div>)}</div>}
        <div className="row"><button className="btn btn-primary">Confirm targets</button></div>
      </form>
    </div>
  );
}
