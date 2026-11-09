import { useEffect, useState } from 'react';
import { api } from '../api';

export interface ClinicianRef { id: string; name: string; type: string; specialty: string | null }
export interface ProcedureRef { id: string; code: string; name: string; service: string; defaultMinutes: number; defaultAnaesthetic: string; sideRequired: boolean; feeCode: string | null; isActive: boolean }

/** Clinician register and procedure catalogue for pick-lists (any signed-in user; no patient information). */
export function useRefData() {
  const [clinicians, setClinicians] = useState<ClinicianRef[]>([]);
  const [procedures, setProcedures] = useState<ProcedureRef[]>([]);
  useEffect(() => {
    api<ClinicianRef[]>('/api/organisation/clinicians').then(setClinicians).catch(() => undefined);
    api<ProcedureRef[]>('/api/organisation/procedures').then(setProcedures).catch(() => undefined);
  }, []);
  return { clinicians, procedures };
}

/** <datalist> elements so name fields suggest from the registers but still accept typing. */
export function RefLists({ clinicians, procedures }: { clinicians: ClinicianRef[]; procedures: ProcedureRef[] }) {
  return (
    <>
      <datalist id="dl-surgeons">{clinicians.filter((c) => c.type !== 'Anaesthetist').map((c) => <option key={c.id} value={c.name}>{c.specialty ?? c.type}</option>)}</datalist>
      <datalist id="dl-anaesthetists">{clinicians.filter((c) => c.type === 'Anaesthetist').map((c) => <option key={c.id} value={c.name} />)}</datalist>
      <datalist id="dl-clinicians">{clinicians.map((c) => <option key={c.id} value={c.name}>{c.type}</option>)}</datalist>
      <datalist id="dl-procedures">{procedures.map((p) => <option key={p.id} value={p.name}>{p.defaultMinutes} min · {p.defaultAnaesthetic}</option>)}</datalist>
    </>
  );
}

/** Finds a catalogue entry by the typed name (case-insensitive). */
export const findProcedure = (list: ProcedureRef[], name: string) => list.find((p) => p.name.toLowerCase() === name.trim().toLowerCase()) ?? null;

let clinicianCache: Promise<ClinicianRef[]> | null = null;
const loadClinicians = () => (clinicianCache ??= api<ClinicianRef[]>('/api/organisation/clinicians').catch(() => { clinicianCache = null; return []; }));

/**
 * Picks a clinician from the register (lists, appointments, referrals and the waitlist store the clinician's id, never a typed name).
 * `role`: 'surgeon' = surgeons/endoscopists/physicians, 'anaesthetist', or 'any'. Shows the current value even if that clinician is now inactive.
 */
export function ClinicianSelect({ label, value, onChange, role = 'any', required = false, currentName }:
  { label: string; value: string | null | undefined; onChange: (id: string) => void; role?: 'surgeon' | 'anaesthetist' | 'any'; required?: boolean; currentName?: string | null }) {
  const [list, setList] = useState<ClinicianRef[]>([]);
  useEffect(() => { loadClinicians().then(setList); }, []);
  const shown = list.filter((c) => role === 'any' || (role === 'anaesthetist' ? c.type === 'Anaesthetist' : c.type !== 'Anaesthetist'));
  const missing = value && !shown.some((c) => c.id === value);
  return (
    <label>{label}
      <select value={value ?? ''} onChange={(e) => onChange(e.target.value)} required={required}>
        <option value="">{required ? 'Choose…' : '— none —'}</option>
        {missing && <option value={value!}>{currentName ?? 'Current clinician'} (inactive)</option>}
        {shown.map((c) => <option key={c.id} value={c.id}>{c.name}{c.specialty ? ` – ${c.specialty}` : ''}</option>)}
      </select>
      {list.length === 0 && <span className="hint">Add clinicians in Organisation set-up › Clinicians.</span>}
    </label>
  );
}
