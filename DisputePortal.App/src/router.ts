import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from './stores/auth'
import { rememberPageScroll, restorePageScroll } from './utils/pageScroll'

const router = createRouter({
  history: createWebHistory(),
  scrollBehavior: (to, _from, savedPosition) => restorePageScroll(to.fullPath, savedPosition),
  routes: [
    { path: '/register', component: () => import('./views/RegistrationView.vue') },
    { path: '/forgot-password', component: () => import('./views/AccessRecoveryView.vue') },
    { path: '/sign-in', name: 'sign-in', component: () => import('./views/SignInView.vue') },
    {
      path: '/personal',
      component: () => import('./views/HomeEntry.vue'),
      meta: { requiresAuth: true, requiresCustomer: true }
    },
    {
      path: '/',
      name: 'dashboard',
      component: () => import('./views/HomeEntry.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/transactions',
      name: 'transactions',
      component: () => import('./views/TransactionsView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/transactions/:id',
      name: 'transaction-detail',
      component: () => import('./views/TransactionDetailView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/disputes',
      name: 'disputes',
      component: () => import('./views/DisputesView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/disputes/new/:transactionId',
      name: 'new-dispute',
      component: () => import('./views/NewDisputeView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/disputes/:id',
      name: 'dispute-detail',
      component: () => import('./views/DisputeDetailView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/profile',
      component: () => import('./views/ProfileView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/notifications',
      name: 'notifications',
      component: () => import('./views/NotificationsView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/support',
      component: () => import('./views/SupportView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/accounts/manage',
      component: () => import('./views/AccountServicesView.vue'),
      meta: { requiresAuth: true, requiresCustomer: true }
    },
    {
      path: '/accounts/:id',
      component: () => import('./views/AccountDetailView.vue'),
      meta: { requiresAuth: true }
    },
    {
      path: '/admin/dispute-operations',
      alias: '/admin/platform',
      component: () => import('./views/PlatformAdministrationView.vue'),
      meta: { requiresAuth: true, requiresSuperAdmin: true }
    },
    {
      path: '/admin/dispute-rules',
      component: () => import('./views/DisputeRulesView.vue'),
      meta: { requiresAuth: true, requiresSuperAdmin: true }
    },
    {
      path: '/admin/account-requests',
      component: () => import('./views/AccountRequestsView.vue'),
      meta: { requiresAuth: true, requiresUsersManage: true }
    },
    {
      path: '/admin/users',
      component: () => import('./views/UserAdministrationView.vue'),
      meta: { requiresAuth: true, requiresUsersManage: true }
    },
    {
      path: '/admin/transactions',
      component: () => import('./views/StaffTransactionsView.vue'),
      meta: { requiresAuth: true, requiresAssistance: true }
    },
    {
      path: '/admin/transactions/:id',
      component: () => import('./views/StaffTransactionDetailView.vue'),
      meta: { requiresAuth: true, requiresAssistance: true }
    },
    {
      path: '/admin/monitoring',
      component: () => import('./views/MonitoringView.vue'),
      meta: { requiresAuth: true, requiresSuperAdmin: true }
    },
    { path: '/admin', redirect: '/admin/disputes' },
    {
      path: '/admin/disputes',
      name: 'admin-disputes',
      component: () => import('./views/AdminQueueView.vue'),
      meta: { requiresAuth: true, requiresDisputeRead: true }
    },
    {
      path: '/admin/disputes/:id',
      name: 'admin-dispute',
      component: () => import('./views/AdminDisputeView.vue'),
      meta: { requiresAuth: true, requiresDisputeRead: true }
    },
    {
      path: '/admin/audit',
      name: 'admin-audit',
      component: () => import('./views/AdminAuditView.vue'),
      meta: { requiresAuth: true, requiresAudit: true }
    },
    { path: '/:pathMatch(.*)*', redirect: '/' }
  ]
})

router.beforeEach(async (to, from) => {
  rememberPageScroll(from.fullPath)
  const auth = useAuthStore()
  await auth.initialize()
  if (auth.user) await auth.refreshSession()
  if (to.path.startsWith('/admin')) auth.setWorkspace('staff')
  else if (
    to.path === '/personal' ||
    to.path.startsWith('/accounts/') ||
    to.path === '/transactions' ||
    to.path.startsWith('/transactions/') ||
    to.path === '/disputes' ||
    to.path.startsWith('/disputes/')
  )
    auth.setWorkspace('personal')
  if (to.path === '/' && auth.isAdmin && auth.workspace === 'staff') return '/admin/disputes'
  if (to.meta.requiresAuth && !auth.user)
    return { name: 'sign-in', query: { redirect: to.fullPath } }
  if (to.name === 'sign-in' && auth.user)
    return auth.isAdmin && auth.workspace === 'staff' ? '/admin/disputes' : '/'
  if (to.meta.requiresCustomer && !auth.user?.roles.includes('customer')) return '/'
  if (to.meta.requiresAssistance && !auth.user?.permissions.includes('transactions.assist'))
    return '/'
  if (to.meta.requiresDisputeRead && !auth.isAdmin) return '/'
  if (to.meta.requiresUsersManage && !auth.user?.permissions.includes('users.manage')) return '/'
  if (to.meta.requiresSuperAdmin && !auth.user?.roles.includes('super_admin')) return '/'
  if (to.meta.requiresAudit && !auth.canReadAudit) return '/admin/disputes'
  return true
})

const routeTitles: Record<string, string> = {
  '/': 'Overview',
  '/personal': 'Overview',
  '/register': 'Create Account',
  '/forgot-password': 'Password Recovery',
  '/sign-in': 'Sign In',
  '/transactions': 'Transactions',
  '/transactions/:id': 'Transaction Details',
  '/disputes': 'Disputes',
  '/disputes/new/:transactionId': 'Create Dispute',
  '/disputes/:id': 'Dispute Details',
  '/profile': 'Profile & Security',
  '/notifications': 'Notifications',
  '/support': 'Support',
  '/accounts/manage': 'Accounts',
  '/accounts/:id': 'Account Details',
  '/admin/dispute-operations': 'Dispute Operations',
  '/admin/platform': 'Dispute Operations',
  '/admin/dispute-rules': 'Dispute Policy',
  '/admin/account-requests': 'Account Requests',
  '/admin/users': 'User Administration',
  '/admin/transactions': 'Customer Transactions',
  '/admin/transactions/:id': 'Customer Assistance',
  '/admin/monitoring': 'API Monitoring',
  '/admin/disputes': 'Case Management',
  '/admin/disputes/:id': 'Case Details',
  '/admin/audit': 'Audit History'
}
router.afterEach((to) => {
  const title = routeTitles[to.matched.at(-1)?.path ?? '']
  document.title = title ? title + ' | Dispute Portal' : 'Dispute Portal'
})

export default router
