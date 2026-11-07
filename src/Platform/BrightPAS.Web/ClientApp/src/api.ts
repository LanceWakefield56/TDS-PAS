// Small API client: same-origin cookies, CSRF header on every call, JSON in and out.
export class ApiError extends Error {
  constructor(public status: number, public body: any) { super(`HTTP ${status}`); }
}

export async function api<T = any>(path: string, options: { method?: string; body?: unknown } = {}): Promise<T> {
  const res = await fetch(path, {
    method: options.method ?? (options.body !== undefined ? 'POST' : 'GET'),
    credentials: 'same-origin',
    headers: { 'X-BrightPAS-Request': '1', ...(options.body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
  });
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (res.status === 401 && !path.startsWith('/api/auth') && !path.startsWith('/api/me')) {
    window.dispatchEvent(new CustomEvent('pas:signed-out'));
  }
  if (!res.ok) throw new ApiError(res.status, data);
  return data as T;
}

export type Access = 'None' | 'ReadOnly' | 'Full';
export interface Me {
  userName: string; displayName: string; roles: string[]; permissions: string[];
  modules: Record<string, Access>;
  licence: { health: string; edition: string | null; developmentKey: boolean; message: string | null };
}
export interface PatientSummary {
  id: string; mrn: string; nhi: string | null; familyName: string; givenNames: string; preferredName: string | null;
  dateOfBirth: string; sex: string; isTestPatient: boolean;
}
export interface Alert { id: string; type: string; severity: string; description: string; recordedBy: string; recordedAt: string }
export interface PatientDetail extends PatientSummary {
  gender: string | null; mobilePhone: string | null; homePhone: string | null; email: string | null;
  addressLine1: string | null; addressLine2: string | null; suburb: string | null; city: string | null; postcode: string | null;
  gpPracticeHpiFacilityId: string | null; ethnicities: string[]; alerts: Alert[];
}
export interface Location { id: string; code: string; name: string; type: string; isActive: boolean }

export const Perm = {
  PatientView: 'patient.view', PatientRegister: 'patient.register', PatientAlerts: 'patient.alerts.manage',
  UsersManage: 'admin.users.manage', AuditView: 'privacy.audit.view', ModulesManage: 'admin.modules.manage', Integration: 'admin.integration.manage',
  LabsView: 'labs.view', LabsAck: 'labs.acknowledge', LabsMatch: 'labs.match',
  TheatreView: 'theatre.view', TheatreBook: 'theatre.book', TheatreManage: 'theatre.manage',
  FormsSend: 'forms.send', FormsTriage: 'forms.triage',
  BillingView: 'billing.view', BillingManage: 'billing.manage', BillingFees: 'billing.fees',
  RemindersView: 'reminders.view', RemindersManage: 'reminders.manage',
} as const;

export const nzDateTime = (iso: string | null | undefined) => iso ? new Date(iso).toLocaleString('en-NZ', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '—';
export const money = (n: number) => n.toLocaleString('en-NZ', { style: 'currency', currency: 'NZD' });
export const todayIso = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };
export const addDays = (iso: string, n: number) => { const [y, m, d] = iso.split('-').map(Number); const x = new Date(y, m - 1, d + n); return `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`; };
export const errText = (e: unknown) => e instanceof ApiError ? (e.body?.errors?.join(' ') ?? e.body?.message ?? (e.status === 403 ? 'You do not have access to this.' : `Something went wrong (${e.status}).`)) : 'Could not reach BrightPAS. Check your connection.';

export function formatDate(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString('en-NZ', { day: '2-digit', month: 'short', year: 'numeric' });
}

export function age(iso: string): number {
  const [y, m, d] = iso.split('-').map(Number);
  const now = new Date();
  let a = now.getFullYear() - y;
  if (now.getMonth() + 1 < m || (now.getMonth() + 1 === m && now.getDate() < d)) a--;
  return a;
}

// Common HISO 10001 level-4 ethnicity codes for quick selection and display; any valid 5-digit code can be entered.
export const ETHNICITY_QUICK: [string, string][] = [
  ['11111', 'New Zealand European'], ['21111', 'Māori'], ['31111', 'Samoan'], ['33111', 'Tongan'], ['42100', 'Chinese'], ['43100', 'Indian'],
];
export const ethnicityName = (code: string) => ETHNICITY_QUICK.find(([k]) => k === code)?.[1] ?? null;
