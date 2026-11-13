export const types = {
  Vacation: { label: 'Concediu de odihnă', colors: ['blue-lighten-4', 'blue-darken-3'] },
  Medical: { label: 'Concediu medical', colors: ['red-lighten-4', 'red-darken-3'] },
  Unpaid: { label: 'Concediu fără plată', colors: ['amber-lighten-4', 'amber-darken-4'] },
  Special: { label: 'Eveniment special', colors: ['purple-lighten-4', 'purple-darken-3'] },
  Remote: { label: 'Lucru remote', colors: ['teal-lighten-4', 'teal-darken-3'] },
} as const
export const statuses = {
  Pending: 'În așteptare', Approved: 'Aprobat', Rejected: 'Respinsă', Canceled: 'Anulată',
} as const
export const weekdays = ['Luni', 'Marți', 'Miercuri', 'Joi', 'Vineri', 'Sâmbătă', 'Duminică']

export interface CalendarEvent {
  requestId: number
  userId: number
  date: string
  type: keyof typeof types
  status: 'Pending' | 'Approved'
}
