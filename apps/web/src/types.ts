export type Isotope = 'F-18' | 'Ga-68'
export type PatientPlan = { id: string; number: string; weight: number; isotope: Isotope; protocol: string; scannerId: string; injectionMinutes: number; scanStartMinutes: number; duration: number; uptakeMinutes: number; patientCategory: 'S' | 'M' | 'F'; coefficient: number; confirmed: boolean }
export type Scanner = { id: string; name: string; model: string; serialNumber?: string | null; manufactureYear?: number | null }
