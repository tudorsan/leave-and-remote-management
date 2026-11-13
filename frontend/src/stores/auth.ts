import { defineStore } from 'pinia'
import { ref } from 'vue'

const tokenKey = 'leaveRemoteToken'

interface CurrentUser {
  userId: string
  role: 'Employee' | 'Manager' | 'Admin'
}

export const useAuthStore = defineStore('auth', () => {
  const user = ref<CurrentUser | null>(null)

  function signOut() {
    sessionStorage.removeItem(tokenKey)
    user.value = null
  }

  async function loadUser(token: string, expectedRole?: CurrentUser['role']) {
    const response = await fetch('/api/auth/me', {
      headers: { Authorization: `Bearer ${token}` },
    })

    if (response.status === 401 || response.status === 403) {
      signOut()
      return false
    }
    if (!response.ok) throw new Error('Sesiunea nu a putut fi verificată.')

    const result = await response.json()
    if (
      !result.isAuthenticated ||
      typeof result.userId !== 'string' ||
      !['Employee', 'Manager', 'Admin'].includes(result.role)
    ) {
      signOut()
      return false
    }

    if (expectedRole && result.role !== expectedRole) return 'role-mismatch'

    user.value = { userId: result.userId, role: result.role }
    return true
  }

  // Fără rol așteptat, sesiunea pornește cu rolul real al contului, confirmat de server.
  async function startSession(token: string, expectedRole?: CurrentUser['role']) {
    const result = await loadUser(token, expectedRole)
    if (result !== true) return result

    sessionStorage.setItem(tokenKey, token)
    return true
  }

  async function restoreSession() {
    if (user.value) return true

    const token = sessionStorage.getItem(tokenKey)
    if (!token) return false

    try {
      return (await loadUser(token)) === true
    } catch {
      return false
    }
  }

  function getToken() {
    return sessionStorage.getItem(tokenKey)
  }

  return { user, startSession, restoreSession, signOut, getToken }
})
