// Small API client: same-origin cookies, CSRF header on every call, JSON in and out.
export class ApiError extends Error {
  constructor(public status: number, public body: any) { super(`HTTP ${status}`); }
}

export async function api<T = any>(path: string, options: { method?: string; body?: unknown } = {}): Promise<T> {
  const res = await fetch(path, {
    method: options.method ?? (options.body !== undefined ? 'POST' : 'GET'),
    credentials: 'same-origin',
    headers: { 'X-Galenica-Request': '1', ...(options.body !== undefined ? { 'Content-Type': 'application/json' } : {}) },
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

export interface Me { userName: string; displayName: string; roles: string[]; permissions: string[] }
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
  UsersManage: 'admin.users.manage', AuditView: 'privacy.audit.view',
} as const;

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
