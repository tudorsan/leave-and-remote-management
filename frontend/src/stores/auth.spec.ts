import { createPinia, setActivePinia } from 'pinia'
import { afterEach, expect, it, vi } from 'vitest'
import { useAuthStore } from './auth'

afterEach(() => {
  sessionStorage.clear()
  vi.unstubAllGlobals()
})

it('starts a session only when the selected role matches the account role', async () => {
  setActivePinia(createPinia())
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ isAuthenticated: true, userId: '42', role: 'Employee' }),
  }))
  const auth = useAuthStore()

  expect(await auth.startSession('demo-token', 'Admin')).toBe('role-mismatch')
  expect(auth.user).toBeNull()
  expect(auth.getToken()).toBeNull()

  expect(await auth.startSession('demo-token', 'Employee')).toBe(true)
  expect(auth.user?.role).toBe('Employee')
  expect(auth.getToken()).toBe('demo-token')
})

it('starts a session with the account role reported by the server when no role is expected', async () => {
  setActivePinia(createPinia())
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
    ok: true,
    json: async () => ({ isAuthenticated: true, userId: '7', role: 'Manager' }),
  }))
  const auth = useAuthStore()

  expect(await auth.startSession('demo-token')).toBe(true)
  expect(auth.user?.role).toBe('Manager')
})
