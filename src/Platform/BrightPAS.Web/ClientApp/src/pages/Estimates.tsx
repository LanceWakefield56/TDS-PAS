import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { api, downloadPost, errText, formatDate, money, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';
import ExportButton from '../components/ExportButton';

// ------------------------------------------------------------------ shared types
export interface AddOnChoice { code: string; name: string; ifRequired: boolean; quantity: number }
export interface ChargeLine { description: string | null; amount: number | null }
export interface EstForm {
  patientId: string | null; estimateDate: string | null; firstName: string; lastName: string; dateOfBirth: string | null; nhi: string | null; phone: string | null;
  email: string | null; address: string | null; town: string | null; postcode: string | null; funding: string; insurer: string | null; policyNumber: string | null;
  priorApproval: string | null; procedureId: string | null; surgeonId: string | null; surgeonName: string | null; procedureDate: string | null; anaesthetic: string;
  plannedMinutes: number | null; useSpecial: boolean; multiplier: number; facilityOverride: number | null; surgeonOverride: number | null; anaesthetistOverride: number | null;
  inputs: { addOns: AddOnChoice[]; otherCharges: ChargeLine[]; adjustment: ChargeLine; ifRequired: ChargeLine[] }; notes: string | null; preparedBy: string | null;
}
interface Meta {
  specialties: { id: string; name: string; defaultNotes: string | null; isActive: boolean }[];
  procedures: { id: string; code: string; specialtyId: string; group: string; name: string; minutes: number; defaultAnaesthetic: string; isActive: boolean }[];
  surgeons: { id: string; name: string; isActive: boolean; specialtyIds: string[] }[];
  anaesthetics: { value: string; label: string; anaesthetist: boolean }[];
  settings: { sendMode: string; holdMinutes: number; incisive: boolean; surgeonFeeLabel: string };
  preparedBy: string; admin: boolean;
}
interface CalcLine { label: string; amount: number | null; display: string | null; note: string | null }
interface Preview {
  result: { title: string; lines: CalcLine[]; additional: CalcLine[]; subtotal: number; deposit: number; balance: number; scheduledMinutes: number; plannedMinutes: number; extraMinutes: number;
    validUntil: string | null; depositDue: string | null; warnings: string[]; costing: { revenue: number; cost: number; margin: number; costingComplete: boolean } | null };
  available: { id: string; code: string; name: string; fee: number; type: string }[];
  included: string[] | null;
}

export const FUNDING: [string, string][] = [['SelfFunding', 'Self-funding'], ['Insurance', 'Insurance'], ['NotYetKnown', 'Not yet known']];
export const statusClass = (s: string) => (s === 'Issued' ? 'green' : s === 'Cancelled' ? 'muted' : '');
const num = (v: string) => (v.trim() === '' ? null : Number(v));
const blankForm = (): EstForm => ({
  patientId: null, estimateDate: todayIso(), firstName: '', lastName: '', dateOfBirth: null, nhi: null, phone: null, email: null, address: null, town: null, postcode: null,
  funding: 'SelfFunding', insurer: null, policyNumber: null, priorApproval: null, procedureId: null, surgeonId: null, surgeonName: null, procedureDate: null, anaesthetic: 'General',
  plannedMinutes: null, useSpecial: false, multiplier: 1, facilityOverride: null, surgeonOverride: null, anaesthetistOverride: null,
  inputs: { addOns: [], otherCharges: [], adjustment: { description: null, amount: null }, ifRequired: [] }, notes: null, preparedBy: null,
});

function useMeta() {
  const [meta, setMeta] = useState<Meta | null>(null);
  useEffect(() => { api<Meta>('/api/estimates/meta').then(setMeta).catch(() => setMeta(null)); }, []);
  return meta;
}

// ------------------------------------------------------------------ list
export default function Estimates() {
  const { can } = useSession();
  const meta = useMeta();
  const [q, setQ] = useState('');
  const [status, setStatus] = useState('');
  const [surgeon, setSurgeon] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(() => {
    const p = new URLSearchParams();
    if (q.trim()) p.set('q', q.trim());
    if (status) p.set('status', status);
    if (surgeon) p.set('surgeonId', surgeon);
    if (from) p.set('from', from);
    if (to) p.set('to', to);
    api<any[]>(`/api/estimates?${p}`).then((r) => { setRows(r); setError(null); }).catch((e) => setError(errText(e)));
  }, [q, status, surgeon, from, to]);
  useEffect(() => { load(); }, [load]);
  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div><h1>Procedure estimates</h1><p className="muted">Build, issue and email patient estimates from the TDS price list.</p></div>
        <div className="row">
          {can(Perm.EstimatesBuild) && <Link className="btn btn-primary" to="/estimates/new">New estimate</Link>}
          {can(Perm.EstimatesBuild) && <Link className="btn btn-light" to="/estimates/outbox">Outbox</Link>}
          {can(Perm.EstimatesManage) && <Link className="btn btn-light" to="/estimates/admin">Admin</Link>}
        </div>
      </div>
      <section className="card stack">
        <div className="form-grid">
          <label>Search<input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Reference, name, NHI, procedure or email" /></label>
          <label>Status<select value={status} onChange={(e) => setStatus(e.target.value)}><option value="">All</option><option>Draft</option><option>Issued</option><option>Cancelled</option></select></label>
          <label>Surgeon<select value={surgeon} onChange={(e) => setSurgeon(e.target.value)}><option value="">All</option>{meta?.surgeons.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
          <label>From<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
          <label>To<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        </div>
        {error && <p className="error" role="alert">{error}</p>}
        {rows && <EstimateTable rows={rows} showPatient />}
      </section>
    </div>
  );
}

export function EstimateTable({ rows, showPatient }: { rows: any[]; showPatient: boolean }) {
  if (rows.length === 0) return <p className="muted">No estimates.</p>;
  return (
    <table className="results">
      <thead><tr><th>Reference</th><th>Date</th>{showPatient && <th>Patient</th>}<th>Procedure</th><th>Surgeon</th><th className="money">Total</th><th>Status</th><th>Sent</th></tr></thead>
      <tbody>{rows.map((r) => (
        <tr key={r.id}>
          <td><Link to={`/estimates/${r.id}`}>{r.ref}</Link></td>
          <td>{formatDate(r.estimateDate)}</td>
          {showPatient && <td>{r.patient}{r.nhi && <span className="muted"> · {r.nhi}</span>}</td>}
          <td>{r.procedure ?? '—'}</td>
          <td>{r.surgeon ?? '—'}</td>
          <td className="money">{money(r.subtotal)}</td>
          <td><span className={`pill ${statusClass(r.status)}`}>{r.status}</span></td>
          <td className="muted">{[r.emailedAt && 'Emailed', r.sentToIncisiveAt && 'Incisive'].filter(Boolean).join(', ') || '—'}</td>
        </tr>
      ))}</tbody>
    </table>
  );
}

// ------------------------------------------------------------------ builder
export function EstimateEdit() {
  const { id } = useParams();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const { can } = useSession();
  const meta = useMeta();
  const [form, setForm] = useState<EstForm | null>(null);
  const [est, setEst] = useState<any>(null);
  const [archive, setArchive] = useState<any[]>([]);
  const [preview, setPreview] = useState<Preview | null>(null);
  const [specialtyId, setSpecialtyId] = useState('');
  const [templates, setTemplates] = useState<any[]>([]);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [emailOpen, setEmailOpen] = useState(false);
  const [cancelOpen, setCancelOpen] = useState(false);
  const [tplName, setTplName] = useState<string | null>(null);
  const readOnly = !can(Perm.EstimatesBuild) || est?.status === 'Cancelled';
  const timer = useRef<number | undefined>(undefined);

  const load = useCallback(async () => {
    try {
      if (id) {
        const r = await api<any>(`/api/estimates/${id}`);
        setEst(r.estimate); setArchive(r.archive);
        setForm({ ...blankForm(), ...r.form, inputs: { ...blankForm().inputs, ...r.form.inputs } });
      } else {
        const pid = params.get('patient');
        const f = await api<EstForm>(`/api/estimates/new${pid ? `?patientId=${pid}` : ''}`);
        setForm({ ...blankForm(), ...f, inputs: { ...blankForm().inputs, ...f.inputs } });
        setEst(null); setArchive([]);
      }
    } catch (e) { setErrors([errText(e)]); }
  }, [id, params]);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => { api<any[]>('/api/estimates/templates').then(setTemplates).catch(() => setTemplates([])); }, []);
  useEffect(() => {
    if (!form || !meta || specialtyId || !form.procedureId) return;
    const p = meta.procedures.find((x) => x.id === form.procedureId);
    if (p) setSpecialtyId(p.specialtyId);
  }, [form, meta, specialtyId]);

  // Live totals and warnings (debounced).
  useEffect(() => {
    if (!form || readOnly && !id) return;
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => { api<Preview>('/api/estimates/preview', { body: form }).then(setPreview).catch(() => undefined); }, 250);
    return () => window.clearTimeout(timer.current);
  }, [form, readOnly, id]);

  const set = (p: Partial<EstForm>) => setForm((f) => (f ? { ...f, ...p } : f));
  const setInputs = (p: Partial<EstForm['inputs']>) => setForm((f) => (f ? { ...f, inputs: { ...f.inputs, ...p } } : f));
  const procs = useMemo(() => (meta?.procedures ?? []).filter((p) => (p.isActive || p.id === form?.procedureId) && (!specialtyId || p.specialtyId === specialtyId)), [meta, specialtyId, form?.procedureId]);
  const groups = useMemo(() => [...new Set(procs.map((p) => p.group || 'Other'))], [procs]);
  const surgeons = useMemo(() => (meta?.surgeons ?? []).filter((s) => s.isActive || s.id === form?.surgeonId)
    .sort((a, b) => Number(b.specialtyIds.includes(specialtyId)) - Number(a.specialtyIds.includes(specialtyId)) || a.name.localeCompare(b.name)), [meta, specialtyId, form?.surgeonId]);

  const chooseProcedure = (pid: string) => {
    const p = meta?.procedures.find((x) => x.id === pid);
    const spec = meta?.specialties.find((s) => s.id === p?.specialtyId);
    set({ procedureId: pid || null, anaesthetic: p?.defaultAnaesthetic ?? form!.anaesthetic, inputs: { ...form!.inputs, addOns: [] }, notes: form!.notes || spec?.defaultNotes || null });
  };
  const applyTemplate = async (tid: string) => {
    if (!tid || !form) return;
    try { const f = await api<EstForm>(`/api/estimates/templates/${tid}/apply`, { body: form }); setForm({ ...form, ...f, inputs: { ...blankForm().inputs, ...f.inputs } }); setSpecialtyId(''); }
    catch (e) { setErrors([errText(e)]); }
  };

  const save = async (issue: boolean) => {
    if (!form) return;
    setBusy(true); setErrors([]); setMsg(null);
    try {
      const r = await api<any>(id ? `/api/estimates/${id}` : '/api/estimates', { method: id ? 'PUT' : 'POST', body: { form, issue } });
      setMsg([issue ? `Issued ${r.ref}.` : `Saved ${r.ref}.`, r.archiveMessage, r.stoppedEmails ? `${r.stoppedEmails} held email(s) stopped because the estimate changed.` : null].filter(Boolean).join(' '));
      if (!id) navigate(`/estimates/${r.id}`, { replace: true }); else await load();
    } catch (e: any) { setErrors(e?.body?.errors ?? [errText(e)]); } finally { setBusy(false); }
  };
  const act = async (path: string, body: unknown, done: (r: any) => void) => {
    setErrors([]); setMsg(null);
    try { done(await api<any>(path, { body })); } catch (e: any) { setErrors(e?.body?.errors ?? [errText(e)]); }
  };

  if (!form || !meta) return <p>{errors.length ? errors.join(' ') : 'Loading…'}</p>;
  const r = preview?.result;
  const anaesthetistNeeded = meta.anaesthetics.find((a) => a.value === form.anaesthetic)?.anaesthetist;
  const addOnName = (code: string) => preview?.available.find((a) => a.code === code)?.name ?? form.inputs.addOns.find((a) => a.code === code)?.name ?? code;
  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div>
          <h1>{est ? est.ref : 'New estimate'} {est && <span className={`pill ${statusClass(est.status)}`}>{est.status}</span>}</h1>
          {est && <p className="muted">Created by {est.createdBy} {nzDateTime(est.createdAt)}{est.issuedAt && ` · issued ${nzDateTime(est.issuedAt)}`}{est.emailedAt && ` · emailed ${nzDateTime(est.emailedAt)}`}{est.sentToIncisiveAt && ` · saved to Incisive ${nzDateTime(est.sentToIncisiveAt)}`}{est.cancelReason && ` · cancelled: ${est.cancelReason}`}</p>}
        </div>
        <Link className="btn btn-light" to="/estimates">All estimates</Link>
      </div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      <div className="est-builder">
        <fieldset className="stack-lg est-form" disabled={readOnly}>
          {!est && templates.length > 0 && (
            <section className="card stack">
              <label>Start from a template (optional)<select value="" onChange={(e) => void applyTemplate(e.target.value)}>
                <option value="">– choose a template –</option>
                {templates.some((t) => t.favourite) && <optgroup label="★ Favourites">{templates.filter((t) => t.favourite).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</optgroup>}
                {[...new Set(templates.filter((t) => !t.favourite).map((t) => t.specialty))].map((g) => <optgroup key={g} label={g}>{templates.filter((t) => !t.favourite && t.specialty === g).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</optgroup>)}
              </select></label>
            </section>
          )}
          <section className="card stack">
            <h2>1. Patient</h2>
            {form.patientId && <p className="hint">Linked to the BrightPAS patient record – <Link to={`/patients/${form.patientId}`}>open record</Link>.</p>}
            <div className="form-grid">
              <label>First name<input value={form.firstName} onChange={(e) => set({ firstName: e.target.value })} /></label>
              <label>Last name<input value={form.lastName} onChange={(e) => set({ lastName: e.target.value })} /></label>
              <label>Date of birth<input type="date" value={form.dateOfBirth ?? ''} onChange={(e) => set({ dateOfBirth: e.target.value || null })} /></label>
              <label>NHI<input value={form.nhi ?? ''} maxLength={7} onChange={(e) => set({ nhi: e.target.value.toUpperCase() || null })} /></label>
              <label>Phone<input value={form.phone ?? ''} onChange={(e) => set({ phone: e.target.value || null })} /></label>
              <label>Email<input type="email" value={form.email ?? ''} onChange={(e) => set({ email: e.target.value || null })} /></label>
              <label>Postal address<input value={form.address ?? ''} onChange={(e) => set({ address: e.target.value || null })} /></label>
              <label>Town<input value={form.town ?? ''} onChange={(e) => set({ town: e.target.value || null })} /></label>
              <label>Postcode<input value={form.postcode ?? ''} onChange={(e) => set({ postcode: e.target.value || null })} /></label>
            </div>
          </section>
          <section className="card stack">
            <h2>2. Funding</h2>
            <div className="form-grid">
              <label>Funding<select value={form.funding} onChange={(e) => set({ funding: e.target.value })}>{FUNDING.map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></label>
              {form.funding === 'Insurance' && <>
                <label>Insurer<input value={form.insurer ?? ''} onChange={(e) => set({ insurer: e.target.value || null })} placeholder="e.g. Southern Cross" /></label>
                <label>Policy / membership number<input value={form.policyNumber ?? ''} onChange={(e) => set({ policyNumber: e.target.value || null })} /></label>
                <label>Prior approval number<input value={form.priorApproval ?? ''} onChange={(e) => set({ priorApproval: e.target.value || null })} /></label>
              </>}
            </div>
          </section>
          <section className="card stack">
            <h2>3. Procedure</h2>
            <div className="form-grid">
              <label>Specialty<select value={specialtyId} onChange={(e) => setSpecialtyId(e.target.value)}><option value="">All specialties</option>{meta.specialties.filter((s) => s.isActive).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
              <label>Procedure<select value={form.procedureId ?? ''} onChange={(e) => chooseProcedure(e.target.value)}>
                <option value="">– choose –</option>
                {groups.map((g) => <optgroup key={g} label={g}>{procs.filter((p) => (p.group || 'Other') === g).map((p) => <option key={p.id} value={p.id}>{p.name}{p.isActive ? '' : ' (retired)'}</option>)}</optgroup>)}
              </select></label>
              <label>Surgeon<select value={form.surgeonId ?? ''} onChange={(e) => set({ surgeonId: e.target.value || null })}>
                <option value="">– other / type below –</option>
                {surgeons.map((s) => <option key={s.id} value={s.id}>{s.name}{!specialtyId || s.specialtyIds.includes(specialtyId) ? '' : ' (other specialty)'}</option>)}
              </select></label>
              {!form.surgeonId && <label>Surgeon name (if not listed)<input value={form.surgeonName ?? ''} onChange={(e) => set({ surgeonName: e.target.value || null })} /></label>}
              <label>Proposed date<input type="date" value={form.procedureDate ?? ''} onChange={(e) => set({ procedureDate: e.target.value || null })} /></label>
              <label>Anaesthetic<select value={form.anaesthetic} onChange={(e) => set({ anaesthetic: e.target.value })}>{meta.anaesthetics.map((a) => <option key={a.value} value={a.value}>{a.label}</option>)}</select></label>
              <label>Planned minutes<input type="number" min={0} value={form.plannedMinutes ?? ''} placeholder={r ? String(r.scheduledMinutes) : ''} onChange={(e) => set({ plannedMinutes: num(e.target.value) })} /></label>
              <label className="check"><input type="checkbox" checked={form.useSpecial} onChange={(e) => set({ useSpecial: e.target.checked })} /> Use special package price (self-funding)</label>
            </div>
          </section>
          <section className="card stack">
            <h2>4. Add-ons</h2>
            {preview?.included && preview.included.length > 0 && <p className="hint">Already included in this procedure (can't be charged again): {preview.included.join(', ')}.</p>}
            {form.inputs.addOns.map((a, i) => (
              <div key={i} className="form-grid">
                <label>Add-on<select value={a.code} onChange={(e) => setInputs({ addOns: form.inputs.addOns.map((x, j) => (j === i ? { ...x, code: e.target.value, name: preview?.available.find((p) => p.code === e.target.value)?.name ?? '' } : x)) })}>
                  <option value="">– choose –</option>
                  {!preview?.available.some((p) => p.code === a.code) && a.code && <option value={a.code}>{addOnName(a.code)} (not available)</option>}
                  {preview?.available.map((p) => <option key={p.code} value={p.code}>{p.name} – {money(p.fee)}{p.type === 'Surgeon' ? ' (surgeon)' : ''}</option>)}
                </select></label>
                <label>Charge<select value={a.ifRequired ? 'req' : 'inc'} onChange={(e) => setInputs({ addOns: form.inputs.addOns.map((x, j) => (j === i ? { ...x, ifRequired: e.target.value === 'req' } : x)) })}><option value="inc">Required</option><option value="req">If required</option></select></label>
                <label>Quantity<input type="number" min={1} max={99} value={a.quantity} onChange={(e) => setInputs({ addOns: form.inputs.addOns.map((x, j) => (j === i ? { ...x, quantity: Number(e.target.value) || 1 } : x)) })} /></label>
                <div className="row"><button type="button" className="btn btn-link danger" onClick={() => setInputs({ addOns: form.inputs.addOns.filter((_, j) => j !== i) })}>Remove</button></div>
              </div>
            ))}
            <div><button type="button" className="btn btn-light" disabled={!form.procedureId} onClick={() => setInputs({ addOns: [...form.inputs.addOns, { code: '', name: '', ifRequired: false, quantity: 1 }] })}>Add an add-on</button></div>
          </section>
          <section className="card stack">
            <h2>5. Other charges, adjustment and "if required" lines</h2>
            <ChargeRows title="Other charges (added to the total)" rows={form.inputs.otherCharges} onChange={(rows) => setInputs({ otherCharges: rows })} />
            <div className="form-grid">
              <label>Adjustment description<input value={form.inputs.adjustment.description ?? ''} onChange={(e) => setInputs({ adjustment: { ...form.inputs.adjustment, description: e.target.value || null } })} placeholder="e.g. Discount" /></label>
              <label>Adjustment amount (negative for a discount)<input type="number" step="0.01" value={form.inputs.adjustment.amount ?? ''} onChange={(e) => setInputs({ adjustment: { ...form.inputs.adjustment, amount: num(e.target.value) } })} /></label>
            </div>
            <ChargeRows title="Additional costs IF required (listed, not totalled)" rows={form.inputs.ifRequired} onChange={(rows) => setInputs({ ifRequired: rows })} />
          </section>
          <section className="card stack">
            <h2>6. Manual fees and notes</h2>
            <div className="form-grid">
              <label>Facility fee (manual)<input type="number" step="0.01" value={form.facilityOverride ?? ''} onChange={(e) => set({ facilityOverride: num(e.target.value) })} /></label>
              <label>Surgeon fee (manual – replaces schedule and surgeon add-ons)<input type="number" step="0.01" value={form.surgeonOverride ?? ''} onChange={(e) => set({ surgeonOverride: num(e.target.value) })} /></label>
              {anaesthetistNeeded && <label>Anaesthetist fee (manual)<input type="number" step="0.01" value={form.anaesthetistOverride ?? ''} onChange={(e) => set({ anaesthetistOverride: num(e.target.value) })} /></label>}
              <label>Whole-estimate multiplier<select value={form.multiplier} onChange={(e) => set({ multiplier: Number(e.target.value) })}><option value={1}>100%</option><option value={2}>200%</option></select></label>
              <label>Estimate date<input type="date" value={form.estimateDate ?? ''} onChange={(e) => set({ estimateDate: e.target.value || null })} /></label>
              <label>Prepared by<input value={form.preparedBy ?? ''} onChange={(e) => set({ preparedBy: e.target.value || null })} /></label>
            </div>
            <label>Notes (printed)<textarea value={form.notes ?? ''} onChange={(e) => set({ notes: e.target.value || null })} /></label>
          </section>
        </fieldset>

        <aside className="est-totals stack">
          <section className="card stack">
            <h2>Estimate</h2>
            {!r ? <p className="muted">Choose a procedure.</p> : <>
              <p><strong>{r.title}</strong></p>
              <table className="totals"><tbody>
                {r.lines.map((l, i) => <tr key={i}><th scope="row">{l.label}</th><td className="money">{l.display ?? money(l.amount ?? 0)}</td></tr>)}
                <tr className="grand"><th scope="row">Subtotal (GST incl)</th><td className="money">{money(r.subtotal)}</td></tr>
                {r.deposit > 0 && <tr><th scope="row">Deposit{r.depositDue && ` by ${formatDate(r.depositDue)}`}</th><td className="money">{money(r.deposit)}</td></tr>}
                {r.deposit > 0 && <tr><th scope="row">Balance</th><td className="money">{money(r.balance)}</td></tr>}
              </tbody></table>
              {r.additional.length > 0 && <><strong>If required</strong><table className="totals"><tbody>{r.additional.map((l, i) => <tr key={i}><th scope="row">{l.label}{l.note && <span className="muted"> – {l.note}</span>}</th><td className="money">{money(l.amount ?? 0)}</td></tr>)}</tbody></table></>}
              <p className="hint">{r.plannedMinutes} min planned ({r.scheduledMinutes} scheduled{r.extraMinutes > 0 && `, ${r.extraMinutes} extra`}){r.validUntil && ` · valid until ${formatDate(r.validUntil)}`}</p>
              {r.costing && <p className="hint">Internal costing (admins only): facility revenue {money(r.costing.revenue)}, cost {money(r.costing.cost)}, margin {money(r.costing.margin)}{!r.costing.costingComplete && ' – costing incomplete'}.</p>}
            </>}
            {r && r.warnings.length > 0 && <div className="warn-box"><ul>{r.warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>}
          </section>
          {!readOnly && (
            <section className="card stack">
              <div className="row">
                {est?.status !== 'Issued' && <button className="btn btn-light" disabled={busy} onClick={() => void save(false)}>Save draft</button>}
                <button className="btn btn-primary" disabled={busy} onClick={() => void save(true)}>{est?.status === 'Issued' ? 'Save changes' : 'Save & issue'}</button>
              </div>
              {est && <div className="row">
                <ExportButton label="PDF" path={`/api/estimates/${id}/pdf`} body={{}} formats={['pdf', 'zip']} />
                <button className="btn btn-light" disabled={est.status !== 'Issued'} title={est.status !== 'Issued' ? 'Issue the estimate first' : ''} onClick={() => setEmailOpen(true)}>Email…</button>
                {meta.settings.incisive && <button className="btn btn-light" disabled={est.status !== 'Issued'} onClick={() => void act(`/api/estimates/${id}/incisive`, {}, (x) => { setMsg(`Saved to the Incisive folder: ${x.file}`); void load(); })}>Save to Incisive</button>}
              </div>}
              {est && <div className="row">
                <button className="btn btn-light" onClick={() => void act(`/api/estimates/${id}/duplicate`, {}, (x) => navigate(`/estimates/${x.id}`))}>Duplicate</button>
                {meta.admin && <button className="btn btn-light" onClick={() => setTplName(form.procedureId ? '' : null)} disabled={!form.procedureId}>Save as template</button>}
                <button className="btn btn-link danger" onClick={() => setCancelOpen(true)}>Cancel estimate</button>
              </div>}
              {tplName !== null && <div className="card subtle stack">
                <label>Template name<input value={tplName} onChange={(e) => setTplName(e.target.value)} /></label>
                <div className="row"><button className="btn btn-primary" onClick={() => void act('/api/estimates/templates', { name: tplName, form }, () => { setTplName(null); setMsg('Template saved.'); })}>Save template</button><button className="btn btn-light" onClick={() => setTplName(null)}>Close</button></div>
              </div>}
              {cancelOpen && <CancelBox onCancel={(reason) => void act(`/api/estimates/${id}/cancel`, { reason }, () => { setCancelOpen(false); void load(); })} onClose={() => setCancelOpen(false)} />}
            </section>
          )}
          {readOnly && est && <section className="card stack"><ExportButton label="PDF" path={`/api/estimates/${id}/pdf`} body={{}} formats={['pdf', 'zip']} /></section>}
          {archive.length > 0 && (
            <section className="card stack">
              <h2>Archived PDFs</h2>
              <p className="hint">Kept when issued and whenever an issued estimate changed – never overwritten.</p>
              <ul className="plain">{archive.map((a) => <li key={a.id}>{nzDateTime(a.createdAt)} · {a.reason} · {a.createdBy} <ExportButton label="Download" className="btn btn-link" path={`/api/estimates/archive/${a.id}`} body={{}} /></li>)}</ul>
            </section>
          )}
        </aside>
      </div>
      {emailOpen && est && <EmailDialog id={est.id} sendMode={meta.settings.sendMode} onClose={(m) => { setEmailOpen(false); if (m) { setMsg(m); void load(); } }} />}
    </div>
  );
}

function ChargeRows({ title, rows, onChange }: { title: string; rows: ChargeLine[]; onChange: (r: ChargeLine[]) => void }) {
  return (
    <div className="stack">
      <strong>{title}</strong>
      {rows.map((c, i) => (
        <div key={i} className="form-grid">
          <label>Description<input value={c.description ?? ''} onChange={(e) => onChange(rows.map((x, j) => (j === i ? { ...x, description: e.target.value || null } : x)))} /></label>
          <label>Amount (GST incl)<input type="number" step="0.01" value={c.amount ?? ''} onChange={(e) => onChange(rows.map((x, j) => (j === i ? { ...x, amount: num(e.target.value) } : x)))} /></label>
          <div className="row"><button type="button" className="btn btn-link danger" onClick={() => onChange(rows.filter((_, j) => j !== i))}>Remove</button></div>
        </div>
      ))}
      <div><button type="button" className="btn btn-light btn-small" onClick={() => onChange([...rows, { description: null, amount: null }])}>Add a line</button></div>
    </div>
  );
}

function CancelBox({ onCancel, onClose }: { onCancel: (reason: string) => void; onClose: () => void }) {
  const [reason, setReason] = useState('');
  return (
    <div className="card subtle stack">
      <label>Reason for cancelling<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
      <div className="row"><button className="btn btn-warn" disabled={!reason.trim()} onClick={() => onCancel(reason)}>Cancel estimate</button><button className="btn btn-light" onClick={onClose}>Keep it</button></div>
    </div>
  );
}

const PASSWORDS: [string, string][] = [['DobShort', 'Date of birth DDMMYY'], ['DobLong', 'Date of birth DDMMYYYY'], ['Nhi', 'NHI (capitals)'], ['Typed', 'Type a password'], ['None', 'No password']];

/** "Check before emailing": recipient, right-recipient checks, PDF password, confirm tick; Outlook draft or send with a hold period. */
function EmailDialog({ id, sendMode, onClose }: { id: string; sendMode: string; onClose: (msg?: string) => void }) {
  const [recipient, setRecipient] = useState('Patient');
  const [to, setTo] = useState<string | null>(null);
  const [check, setCheck] = useState<any>(null);
  const [password, setPassword] = useState('');
  const [typed, setTyped] = useState('');
  const [confirmed, setConfirmed] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    const p = new URLSearchParams({ recipient });
    if (to !== null) p.set('to', to);
    api<any>(`/api/estimates/${id}/email-check?${p}`).then((c) => { setCheck(c); if (to === null) setTo(c.to ?? ''); setPassword((pw) => pw || c.password); }).catch((e) => setError(errText(e)));
  }, [id, recipient, to]);
  const stop = check?.checks.some((c: any) => c.level === 'Stop');
  const body = { recipient, to: to ?? '', password, typedPassword: password === 'Typed' ? typed : null, confirmed };
  const send = async (draft: boolean) => {
    setBusy(true); setError(null);
    try {
      if (draft) { const name = await downloadPost(`/api/estimates/${id}/email/draft`, body); onClose(`Outlook draft saved (${name}) – open it, check it and send it from Outlook.`); }
      else { const r = await api<any>(`/api/estimates/${id}/email/send`, { body }); onClose(`Email to ${r.toAddress} is held in the Outbox until ${nzDateTime(r.sendAfter)} – you can stop it until then.`); }
    } catch (e) { setError(errText(e)); } finally { setBusy(false); }
  };
  return (
    <div className="card stack">
      <h2>Check before emailing {check?.ref}</h2>
      <div className="form-grid">
        <label>Send to<select value={recipient} onChange={(e) => { setRecipient(e.target.value); setTo(null); setPassword(''); setConfirmed(false); }}><option value="Patient">Patient</option><option value="Insurer">Insurer</option><option value="Other">Other</option></select></label>
        <label>Email address<input type="email" value={to ?? ''} onChange={(e) => { setTo(e.target.value); setConfirmed(false); }} list="est-insurers" /></label>
        <datalist id="est-insurers">{check?.insurers.map((i: any) => <option key={i.address} value={i.address}>{i.name}</option>)}</datalist>
        <label>PDF password<select value={password} onChange={(e) => setPassword(e.target.value)}>{PASSWORDS.map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></label>
        {password === 'Typed' && <label>Password (at least 6 characters)<input type="password" autoComplete="new-password" value={typed} onChange={(e) => setTyped(e.target.value)} /></label>}
      </div>
      {password !== 'None' && <p className="hint">The email tells the recipient the rule, never the password.</p>}
      {check?.checks.length > 0 && <ul className="plain">{check.checks.map((c: any) => <li key={c.message} className={c.level === 'Stop' ? 'error' : c.level === 'Warning' ? 'warn-box' : 'hint'}>{c.message}</li>)}</ul>}
      <label className="check"><input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} /> I have checked this is the right patient and the right email address.</label>
      {error && <p className="error" role="alert">{error}</p>}
      <div className="row">
        {sendMode === 'SendFromApp'
          ? <button className="btn btn-primary" disabled={busy || stop || !confirmed} onClick={() => void send(false)}>Send (held {check?.holdMinutes ?? 10} min)</button>
          : <button className="btn btn-primary" disabled={busy || stop || !confirmed} onClick={() => void send(true)}>Create Outlook draft</button>}
        {sendMode === 'SendFromApp' && <button className="btn btn-light" disabled={busy || stop || !confirmed} onClick={() => void send(true)}>Outlook draft instead</button>}
        <button className="btn btn-light" onClick={() => onClose()}>Close</button>
      </div>
    </div>
  );
}

// ------------------------------------------------------------------ outbox
export function EstimatesOutbox() {
  const [all, setAll] = useState(false);
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(() => { api<any[]>(`/api/estimates/outbox${all ? '?all=true' : ''}`).then(setRows).catch((e) => setError(errText(e))); }, [all]);
  useEffect(() => { load(); const t = window.setInterval(load, 20000); return () => window.clearInterval(t); }, [load]);
  const act = async (id: string, what: string) => { setError(null); try { await api(`/api/estimates/outbox/${id}/${what}`, { body: {} }); load(); } catch (e) { setError(errText(e)); } };
  return (
    <div className="stack-lg">
      <div className="page-head row-between"><div><h1>Estimate outbox</h1><p className="muted">Emails sent from BrightPAS wait here for the hold period and can be stopped. Editing an estimate stops its held email.</p></div><Link className="btn btn-light" to="/estimates">All estimates</Link></div>
      <section className="card stack">
        <label className="check"><input type="checkbox" checked={all} onChange={(e) => setAll(e.target.checked)} /> Show sent and stopped emails (last 30 days)</label>
        {error && <p className="error" role="alert">{error}</p>}
        {rows && (rows.length === 0 ? <p className="muted">Nothing waiting.</p> : (
          <table className="results">
            <thead><tr><th>Estimate</th><th>To</th><th>Status</th><th>Sends / sent</th><th>By</th><th></th></tr></thead>
            <tbody>{rows.map((o) => (
              <tr key={o.id}>
                <td><Link to={`/estimates/${o.estimateId}`}>{o.estimateRef}</Link><br /><span className="muted">{o.patientName}</span></td>
                <td>{o.toAddress}{o.passwordProtected && <span className="muted"> · password protected</span>}</td>
                <td><span className={`pill ${o.status === 'Sent' ? 'green' : ''}`}>{o.status}</span>{o.error && <><br /><span className="error">{o.error}</span></>}</td>
                <td>{o.status === 'Sent' ? nzDateTime(o.sentAt) : o.status === 'Stopped' ? `Stopped ${nzDateTime(o.stoppedAt)} by ${o.stoppedBy}` : nzDateTime(o.sendAfter)}</td>
                <td>{o.createdBy}</td>
                <td>{o.status === 'Held' && <div className="row"><button className="btn btn-warn btn-small" onClick={() => void act(o.id, 'stop')}>Stop</button><button className="btn btn-light btn-small" onClick={() => void act(o.id, 'send-now')}>Send now</button></div>}</td>
              </tr>
            ))}</tbody>
          </table>
        ))}
      </section>
    </div>
  );
}

/** Estimates panel on the patient's record: their estimates with open / duplicate, and "New estimate" prefilled from the record. */
export function PatientEstimates({ patientId }: { patientId: string }) {
  const { can } = useSession();
  const navigate = useNavigate();
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api<any[]>(`/api/estimates?patientId=${patientId}`).then(setRows).catch(() => setRows([])); }, [patientId]);
  const dup = async (id: string) => { try { const r = await api<any>(`/api/estimates/${id}/duplicate`, { body: {} }); navigate(`/estimates/${r.id}`); } catch (e) { setError(errText(e)); } };
  if (!rows) return null;
  return (
    <section className="card">
      <div className="row-between"><h2>Procedure estimates</h2>{can(Perm.EstimatesBuild) && <Link className="btn btn-light btn-small" to={`/estimates/new?patient=${patientId}`}>New estimate</Link>}</div>
      {error && <p className="error">{error}</p>}
      {rows.length === 0 ? <p className="muted">None.</p> : (
        <table className="results compact"><thead><tr><th>Reference</th><th>Procedure</th><th>Date</th><th>Status</th><th className="money">Total</th><th></th></tr></thead>
          <tbody>{rows.map((r) => (
            <tr key={r.id}><td><Link to={`/estimates/${r.id}`}>{r.ref}</Link></td><td>{r.procedure ?? '—'}</td><td>{formatDate(r.estimateDate)}</td>
              <td><span className={`pill ${statusClass(r.status)}`}>{r.status}</span></td><td className="money">{money(r.subtotal)}</td>
              <td>{can(Perm.EstimatesBuild) && <button className="btn btn-link" onClick={() => void dup(r.id)}>Duplicate</button>}</td></tr>
          ))}</tbody></table>
      )}
    </section>
  );
}
