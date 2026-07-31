import type { PatientPlan, Scanner } from './types'

export const scanners: Scanner[] = [
  { id: 'ge', name: 'GE Discovery IQ', model: 'ПЭТ/КТ' },
  { id: 'siemens', name: 'Siemens Horizon', model: 'ПЭТ/КТ' },
  { id: 'pet3', name: 'ПЭТ/КТ 3', model: 'ПЭТ/КТ' },
]

export const initialPlans: PatientPlan[] = [
  { id: '1', number: 'Р-2026-00123', weight: 82, isotope: 'F-18', protocol: 'Онко-ПЭТ/КТ (FDG)', scannerId: 'ge', startMinutes: 8 * 60 + 45, duration: 90, confirmed: true },
  { id: '2', number: 'Р-2026-00124', weight: 67, isotope: 'Ga-68', protocol: 'ПСМА-ПЭТ/КТ', scannerId: 'siemens', startMinutes: 9 * 60 + 30, duration: 75, confirmed: true },
  { id: '3', number: 'Р-2026-00125', weight: 74, isotope: 'F-18', protocol: 'Онко-ПЭТ/КТ (FDG)', scannerId: 'pet3', startMinutes: 10 * 60 + 30, duration: 90, confirmed: false },
  { id: '4', number: 'Р-2026-00126', weight: 55, isotope: 'Ga-68', protocol: 'НЭО-ПЭТ/КТ (DOTA TATE)', scannerId: 'ge', startMinutes: 12 * 60 + 30, duration: 80, confirmed: false },
  { id: '5', number: 'Р-2026-00127', weight: 90, isotope: 'F-18', protocol: 'Онко-ПЭТ/КТ (FDG)', scannerId: 'siemens', startMinutes: 13 * 60 + 30, duration: 90, confirmed: false },
  { id: '6', number: 'Р-2026-00128', weight: 68, isotope: 'Ga-68', protocol: 'ПСМА-ПЭТ/КТ', scannerId: 'pet3', startMinutes: 14 * 60 + 30, duration: 75, confirmed: false },
]

export const isotopeConfig: Record<import('./types').Isotope, { halfLife: number; coefficient: number; color: string }> = {
  'F-18': { halfLife: 109.77, coefficient: 3.7, color: '#1769e0' },
  'Ga-68': { halfLife: 68, coefficient: 2.2, color: '#0ea5a4' },
} as const
