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
