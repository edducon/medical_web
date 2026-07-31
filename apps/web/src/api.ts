export type Session = { email: string; role: 'administrator' | 'operator' }
export type ApiScanner = { id: string; name: string; model: string }
export type ApiAppointment = { id: string; scannerId: string; scannerName: string; patientNumber: string; weightKg: number; isotopeCode: 'F-18' | 'Ga-68'; protocolName: string; injectionAt: string; durationMinutes: number; confirmed: boolean }
export type ApiShift = { id: string; shiftDate: string; isotopeCode: 'F-18' | 'Ga-68'; sourceActivityMbq: number; sourceMeasuredAt: string; appointments: ApiAppointment[] }
export type AppointmentDraft = { scannerId: string; patientNumber: string; weightKg: number; protocolName: string; injectionAt: string; durationMinutes: number }
export type IsotopeSettings = { isotopeCode: 'F-18' | 'Ga-68'; halfLifeMinutes: number; doseCoefficientMbqPerKg: number; defaultSourceActivityMbq: number }
export type Protocol = { id: string; isotopeCode: 'F-18' | 'Ga-68'; name: string; durationMinutes: number; isActive: boolean }
export type ShiftSummary = { id: string; shiftDate: string; isotopeCode: 'F-18' | 'Ga-68'; sourceActivityMbq: number; appointmentCount: number; confirmedCount: number }
export type User = { id: string; email: string; role: 'administrator' | 'operator'; createdAt: string }

type SetupStatus = { needsSetup: boolean }
const apiUrl = import.meta.env.VITE_API_URL ?? '/api'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiUrl}${path}`, { credentials: 'include', ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } })
  if (!response.ok) {
    const body = response.headers.get('content-type')?.includes('application/json') ? await response.json().catch(() => undefined) as { error?: string } | undefined : undefined
    throw new Error(body?.error ?? (response.status === 401 ? 'Войдите в систему заново.' : 'Не удалось выполнить запрос. Повторите попытку.'))
  }
  if (response.status !== 204 && !response.headers.get('content-type')?.includes('application/json')) throw new Error('API недоступен. Запустите приложение через Docker Compose.')
  return response.status === 204 ? undefined as T : response.json() as Promise<T>
}

export async function getSession(): Promise<Session | null> {
  const response = await fetch(`${apiUrl}/auth/me`, { credentials: 'include' })
  if (response.status === 401) return null
  if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Не удалось подключиться к серверу.')
  return response.json() as Promise<Session>
}

export function login(email: string, password: string) { return request<Session>('/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }) }
export function logout() { return request<void>('/auth/logout', { method: 'POST' }) }
export function getSetupStatus() { return request<SetupStatus>('/setup/status') }
export async function setup(email: string, password: string, setupToken: string) { await request<void>('/setup', { method: 'POST', body: JSON.stringify({ email, password, setupToken }) }); return login(email, password) }
export function getScanners() { return request<ApiScanner[]>('/scanners') }
export function addScanner(name: string, model: string) { return request<ApiScanner>('/scanners', { method: 'POST', body: JSON.stringify({ name, model }) }) }

export async function getShift(date: string, isotope: 'F-18' | 'Ga-68'): Promise<ApiShift | null> {
  const response = await fetch(`${apiUrl}/shifts/${date}/${isotope}`, { credentials: 'include' })
  if (response.status === 404) return null
  if (!response.ok) throw new Error('Не удалось загрузить смену.')
  return response.json() as Promise<ApiShift>
}

export function createShift(shiftDate: string, isotopeCode: 'F-18' | 'Ga-68', sourceActivityMbq: number, appointment: AppointmentDraft) {
  return request<ApiShift>('/shifts', { method: 'POST', body: JSON.stringify({ shiftDate, isotopeCode, sourceActivityMbq, sourceMeasuredAt: new Date(`${shiftDate}T08:00:00`).toISOString(), appointments: [appointment] }) })
}
export function addAppointment(shiftId: string, appointment: AppointmentDraft) { return request<ApiAppointment>(`/shifts/${shiftId}/appointments`, { method: 'POST', body: JSON.stringify(appointment) }) }
export function updateSourceActivity(shiftId: string, sourceActivityMbq: number) { return request<void>(`/shifts/${shiftId}/source-activity`, { method: 'PUT', body: JSON.stringify({ sourceActivityMbq, sourceMeasuredAt: new Date().toISOString() }) }) }
export function confirmAppointment(appointmentId: string) { return request<void>(`/appointments/${appointmentId}/confirm`, { method: 'POST' }) }
export function reportUrl(shiftId: string) { return `${apiUrl}/shifts/${shiftId}/report` }
export function getIsotopeSettings() { return request<IsotopeSettings[]>('/settings/isotopes') }
export function updateIsotopeSettings(code: string, settings: Omit<IsotopeSettings, 'isotopeCode'>) { return request<void>(`/settings/isotopes/${code}`, { method: 'PUT', body: JSON.stringify(settings) }) }
export function getProtocols() { return request<Protocol[]>('/protocols') }
export function addProtocol(protocol: Omit<Protocol, 'id'>) { return request<Protocol>('/protocols', { method: 'POST', body: JSON.stringify(protocol) }) }
export function getHistory(from: string, to: string) { return request<ShiftSummary[]>(`/shifts?from=${from}&to=${to}`) }
export function getUsers() { return request<User[]>('/users') }
export function createUser(email: string, password: string, role: User['role']) { return request<User>('/users', { method: 'POST', body: JSON.stringify({ email, password, role }) }) }
export function updateAppointment(id: string, appointment: AppointmentDraft) { return request<ApiAppointment>(`/appointments/${id}`, { method: 'PUT', body: JSON.stringify(appointment) }) }
export function deleteAppointment(id: string) { return request<void>(`/appointments/${id}`, { method: 'DELETE' }) }
