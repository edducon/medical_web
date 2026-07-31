export type Isotope = 'F-18' | 'Ga-68'

export type PatientPlan = {
  id: string
  number: string
  weight: number
  isotope: Isotope
  protocol: string
  scannerId: string
  startMinutes: number
  duration: number
  confirmed: boolean
}

export type Scanner = {
  id: string
  name: string
  model: string
}
