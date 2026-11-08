import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { addDays, api, errText, Perm, todayIso } from '../api';
import { useSession } from '../App';
import ExportButton from '../components/ExportButton';

/** NBRS extract for Health NZ: publicly funded waitlist entries. Codes are set here by an administrator (not at install). */
export default function Nbrs() {
  const { can } = useSession();
  const first = todayIso().slice(0, 8) + '01';
  const [from, setFrom] = useState(addDays(first, -1).slice(0, 8) + '01');
  const [to, setTo] = useState(addDays(first, -1));
  const [codes, setCodes] = useState<any>(null);
  const [check, setCheck] = useState<any>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api('/api/waitlist/nbrs/codes').then(setCodes).catch((e) => setError(errText(e))); }, []);
  const saveCodes = async () => { setError(null); setMsg(null); try { await api('/api/waitlist/nbrs/codes', { method: 'PUT', body: codes }); setMsg('Codes saved.'); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); } };
  const run = async () => { setError(null); try { setCheck(await api('/api/waitlist/nbrs/check', { body: { from, to } })); } catch (x) { setError(errText(x)); } };
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>NBRS extract</h1><p className="muted">Publicly funded (Health NZ) waitlist entries for the National Booking Reporting System. <Link to="/waitlist">Back to the waitlist</Link></p></div>
      {codes && <section className="card stack"><h2>Codes from Health NZ</h2>
        <div className="form-grid">
          {([['agencyCode', 'Agency code'], ['facilityCode', 'Facility code'], ['specialtySurgical', 'Health specialty code – surgical'], ['specialtyEndoscopy', 'Health specialty code – endoscopy']] as [string, string][]).map(([k, t]) =>
            <label key={k}>{t}<input value={codes[k] ?? ''} disabled={!can(Perm.ReferralsTriage)} onChange={(e) => setCodes({ ...codes, [k]: e.target.value.toUpperCase() })} /></label>)}
        </div>
        {can(Perm.ReferralsTriage) && <button className="btn btn-light" onClick={saveCodes}>Save codes</button>}
      </section>}
      <section className="card stack"><h2>Extract</h2>
        <div className="row"><label>From<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label><label>To<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
          <button className="btn btn-light" onClick={run}>Check</button><ExportButton label="Download extract" path="/api/waitlist/nbrs/export" body={{ from, to }} /></div>
        {check && <div><p>{check.rows} row(s) ready.</p>{check.problems.length > 0 && <div className="warn-box"><strong>Fix before sending:</strong><ul>{check.problems.map((p: string) => <li key={p}>{p}</li>)}</ul></div>}</div>}
        <p className="hint">Check the column layout and code sets against the current Health NZ NBRS file specification before the first submission.</p>
      </section>
      {msg && <p className="ok-text">{msg}</p>}
      {error && <p className="error" role="alert">{error}</p>}
    </div>
  );
}
