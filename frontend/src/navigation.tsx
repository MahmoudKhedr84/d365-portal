import type { ReactElement } from 'react';
import {
  BookOpen20Regular,
  Building20Regular,
  Cart20Regular,
  ClipboardTask20Regular,
  DocumentBulletList20Regular,
  History20Regular,
  Home20Regular,
  People20Regular,
  PlugConnected20Regular,
} from '@fluentui/react-icons';
import type { Role } from '@/api/session';

export type NavAudience = 'tenantUser' | 'tenantAdmin' | 'superAdmin';

export interface NavItem {
  path: string;
  labelKey: string;
  icon: ReactElement;
  audience: NavAudience;
  section: 'workspaces' | 'administration' | 'platform';
  /** Build-plan step that delivers this page (shown on placeholders). */
  step?: number;
  /** Short description for the home tiles. */
  descriptionKey?: string;
}

export const NAV_ITEMS: NavItem[] = [
  { path: '/', labelKey: 'nav.home', icon: <Home20Regular />, audience: 'tenantUser', section: 'workspaces' },
  { path: '/purchase-requisitions', labelKey: 'nav.purchaseRequisitions', icon: <ClipboardTask20Regular />, audience: 'tenantUser', section: 'workspaces', step: 4 },
  { path: '/purchase-orders', labelKey: 'nav.purchaseOrders', icon: <Cart20Regular />, audience: 'tenantUser', section: 'workspaces', step: 5 },
  { path: '/cases', labelKey: 'nav.cases', icon: <DocumentBulletList20Regular />, audience: 'tenantUser', section: 'workspaces', step: 6 },
  { path: '/general-journals', labelKey: 'nav.generalJournals', icon: <BookOpen20Regular />, audience: 'tenantUser', section: 'workspaces', step: 7 },
  { path: '/admin/users', labelKey: 'nav.users', icon: <People20Regular />, audience: 'tenantAdmin', section: 'administration', step: 2 },
  { path: '/admin/connection', labelKey: 'nav.d365Connection', icon: <PlugConnected20Regular />, audience: 'tenantAdmin', section: 'administration', step: 2 },
  { path: '/admin/audit', labelKey: 'nav.auditLog', icon: <History20Regular />, audience: 'tenantAdmin', section: 'administration', step: 8 },
  { path: '/platform/tenants', labelKey: 'nav.tenants', icon: <Building20Regular />, audience: 'superAdmin', section: 'platform', step: 2 },
];

export interface Viewer {
  role: Role | null; // null = no tenant selected
  isSuperAdmin: boolean;
}

export function canSee(item: NavItem, viewer: Viewer): boolean {
  switch (item.audience) {
    case 'tenantUser':
      return viewer.role !== null;
    case 'tenantAdmin':
      return viewer.role === 'TenantAdmin';
    case 'superAdmin':
      return viewer.isSuperAdmin;
  }
}

export const visibleNav = (viewer: Viewer) => NAV_ITEMS.filter((i) => canSee(i, viewer));
