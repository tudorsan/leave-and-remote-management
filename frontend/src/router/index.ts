
import { createRouter, createWebHistory } from 'vue-router'
import HomeView from '../views/HomeView.vue'
import LoginView from '../views/LoginView.vue'
import StartView from '../views/StartView.vue'
import { useAuthStore } from '../stores/auth'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'start',
      component: StartView,
    },
    {
      path: '/calendar',
      name: 'home',
      component: HomeView,
      meta: { requiresAuth: true },
    },
    {
      path: '/login',
      name: 'login',
      component: LoginView,
    },
    {
      path: '/forgot-password',
      name: 'forgot-password',
      component: () => import('../views/ForgotPasswordView.vue'),
      meta: { public: true },
    },
    {
      path: '/reset-password',
      name: 'reset-password',
      component: () => import('../views/ResetPasswordView.vue'),
      meta: { public: true },
    },
  ],
})

router.beforeEach(async (to) => {
  // Paginile publice (start, resetarea parolei) nu cer și nu verifică o sesiune.
  if (to.name === 'start' || to.meta.public) return
  const authenticated = await useAuthStore().restoreSession()

  if (to.meta.requiresAuth && !authenticated) return { name: 'login' }
  if (to.name === 'login' && authenticated) return { name: 'home' }
})

export default router
