import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { addDays, api, errText, formatDate, Perm, todayIso } from '../api';
import { useSession } from '../App';
import { RefLists, useRefData, ClinicianSelect } from '../components/RefData';
import Modal from '../components/Modal';

interface Tpl { id: string; locationId: string; locationName: string; dayOfWeek: string; startTime: string; endTime: string; type: string; surgeonId: string; surgeon: string; anaesthetistId: string | null; anaesthetist: string | null; everyWeeks: number; startsOn: string; endsOn: string | null; isActive: boolean }
interface Loc { id: string; name: string }
const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const hm = (t: string) => t.slice(0, 5);

/** Regular theatre lists (e.g. every Tuesday morning) and creating lists from them for weeks ahead. */
export default function ListTemplates() {
  const { can } = useSession();
  const manage = can(Perm.TheatreManage);
  const ref = useRefData();
  const [rows, setRows] = useState<Tpl[] | null>(null);
  const [locs, setLocs] = useState<Loc[]>([]);
  const [edit, setEdit] = useState<any | null>(null);
  const [gen, setGen] = useState({ from: todayIso(), to: addDays(todayIso(), 27) });
  const [result, setResult] = useState<{ created: number; skipped: string[] } | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => api<Tpl[]>('/api/theatre/templates').then(setRows).catch((e) => setErrors([errText(e)]));
  useEffect(() => { load(); api<(Loc & { type: string; isActive: boolean })[]>('/api/organisation/locations').then((l) => setLocs(l.filter((x) => x.isActive && (x.type === 'Theatre' || x.type === 'ProcedureRoom')))).catch(() => undefined); }, []);
  const save = async () => {
    setErrors([]);
    try {
      await api('/api/theatre/templates', { body: { ...edit, locationId: edit.locationId || locs[0]?.id, startTime: edit.startTime.slice(0, 5) + ':00', endTime: edit.endTime.slice(0, 5) + ':00', everyWeeks: Number(edit.everyWeeks), endsOn: edit.endsOn || null, anaesthetistId: edit.anaesthetistId || null } });
      setEdit(null); load();
    } catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  const generate = async () => {
    setErrors([]); setResult(null);
    try { setResult(await api('/api/theatre/templates/generate', { body: gen })); } catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setEdit({ ...edit, [k]: e.target.value });
  return (
    <div className="stack-lg">
      <RefLists {...ref} />
      <div className="row-between">
        <div className="page-head">{can(Perm.TheatreView) && <p className="small"><Link to="/theatre">← Theatre lists</Link></p>}<h1>Regular list templates</h1><p className="muted">Each surgeon's regular sessions. Create the real lists for the coming weeks in one step; existing lists are never changed.</p></div>
        {manage && !edit && <button className="btn btn-primary" onClick={() => setEdit({ locationId: locs[0]?.id, dayOfWeek: 'Tuesday', startTime: '08:00', endTime: '12:30', type: 'Surgical', surgeonId: '', anaesthetistId: '', everyWeeks: 1, startsOn: todayIso(), endsOn: '', isActive: true })}>Add template</button>}
      </div>
      {edit && (
        <Modal title={edit.id ? 'Edit template' : 'New template'} onClose={() => { setEdit(null); setErrors([]); }}>
          <div className="form-grid">
            <label>Theatre / room<select value={edit.locationId || locs[0]?.id || ''} onChange={set('locationId')}>{locs.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
            <label>Day<select value={edit.dayOfWeek} onChange={set('dayOfWeek')}>{DAYS.map((d) => <option key={d}>{d}</option>)}</select></label>
            <label>Starts<input type="time" value={edit.startTime.slice(0, 5)} onChange={set('startTime')} /></label>
            <label>Finishes<input type="time" value={edit.endTime.slice(0, 5)} onChange={set('endTime')} /></label>
            <label>Type<select value={edit.type} onChange={set('type')}>{['Surgical', 'Endoscopy', 'Mixed'].map((t) => <option key={t}>{t}</option>)}</select></label>
            <ClinicianSelect label="Surgeon / endoscopist" role="surgeon" required value={edit.surgeonId} currentName={edit.surgeon} onChange={(id) => setEdit({ ...edit, surgeonId: id })} />
            <ClinicianSelect label="Anaesthetist" role="anaesthetist" value={edit.anaesthetistId} currentName={edit.anaesthetist} onChange={(id) => setEdit({ ...edit, anaesthetistId: id })} />
            <label>Repeat<select value={edit.everyWeeks} onChange={set('everyWeeks')}>{[1, 2, 3, 4].map((n) => <option key={n} value={n}>{n === 1 ? 'Every week' : `Every ${n} weeks`}</option>)}</select></label>
            <label>From<input type="date" value={edit.startsOn} onChange={set('startsOn')} /></label>
            <label>Until (optional)<input type="date" value={edit.endsOn ?? ''} onChange={set('endsOn')} /></label>
            <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label>
          </div>
          {errors.length > 0 && <div className="error" role="alert">{errors.map((e) => <div key={e}>{e}</div>)}</div>}
          <div className="row"><button className="btn btn-primary" onClick={save}>Save template</button><button className="btn btn-light" onClick={() => { setEdit(null); setErrors([]); }}>Cancel</button></div>
        </Modal>
      )}
      <section className="card">
        {rows?.length === 0 && <p className="muted">No templates yet.</p>}
        {rows && rows.length > 0 && (
          <table className="results compact">
            <thead><tr><th>Day</th><th>Time</th><th>Room</th><th>Surgeon</th><th>Repeat</th><th>Status</th><th></th></tr></thead>
            <tbody>{rows.map((t) => (
              <tr key={t.id} className={t.isActive ? '' : 'muted'}><td data-label="Day"><strong>{t.dayOfWeek}</strong></td><td data-label="Time">{hm(t.startTime)}–{hm(t.endTime)}</td>
                <td data-label="Room">{t.locationName}</td><td data-label="Surgeon">{t.surgeon}<div className="small muted">{t.type}{t.anaesthetist ? ` · ${t.anaesthetist}` : ''}</div></td>
                <td data-label="Repeat">{t.everyWeeks === 1 ? 'Weekly' : `Every ${t.everyWeeks} weeks`}<div className="small muted">from {formatDate(t.startsOn)}{t.endsOn ? ` to ${formatDate(t.endsOn)}` : ''}</div></td>
                <td data-label="Status"><span className={`pill ${t.isActive ? 'green' : ''}`}>{t.isActive ? 'Active' : 'Off'}</span></td>
                <td>{manage && <button className="btn btn-link" onClick={() => { setErrors([]); setEdit({ ...t, anaesthetistId: t.anaesthetistId ?? '', endsOn: t.endsOn ?? '' }); }}>Edit</button>}</td></tr>
            ))}</tbody>
          </table>
        )}
      </section>
      {manage && rows && rows.length > 0 && (
        <section className="card stack">
          <h2>Create lists from the templates</h2>
          <div className="row">
            <label>From<input type="date" value={gen.from} onChange={(e) => setGen({ ...gen, from: e.target.value })} /></label>
            <label>To<input type="date" value={gen.to} onChange={(e) => setGen({ ...gen, to: e.target.value })} /></label>
            <button className="btn btn-primary" onClick={generate}>Create lists</button>
          </div>
          <p className="hint">Up to 26 weeks at a time. Days that already have a list in that room at that time are skipped.</p>
          {result && <div className="notice" role="status"><strong>{result.created} list{result.created === 1 ? '' : 's'} created.</strong>{result.skipped.length > 0 && <> {result.skipped.length} skipped:<ul>{result.skipped.slice(0, 12).map((s) => <li key={s}>{s}</li>)}</ul></>}</div>}
        </section>
      )}
    </div>
  );
}
