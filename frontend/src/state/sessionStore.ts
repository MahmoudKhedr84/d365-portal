import { create } from 'zustand';
import type { Me, Membership } from '@/api/session';

const TENANT_KEY = 'd365portal.tenantId';

function readStoredTenant(): string | null {
  try {
    return sessionStorage.getItem(TENANT_KEY);
  } catch {
    return null;
  }
}

function writeStoredTenant(id: string | null) {
  try {
    if (id) sessionStorage.setItem(TENANT_KEY, id);
    else sessionStorage.removeItem(TENANT_KEY);
  } catch {
    /* storage unavailable */
  }
}

/**
 * Decides which tenant to open after sign-in:
 *  - exactly one membership → that one;
 *  - a previously chosen tenant that is still valid → that one;
 *  - otherwise null → show the tenant picker (or Not authorized when there are none).
 */
export function pickInitialTenant(me: Me, storedTenantId: string | null): string | null {
  if (me.memberships.length === 1) return me.memberships[0]!.tenantId;
  if (storedTenantId && me.memberships.some((m) => m.tenantId === storedTenantId)) return storedTenantId;
  return null;
}

interface SessionState {
  me: Me | null;
  tenantId: string | null;
  setMe: (me: Me) => void;
  selectTenant: (tenantId: string) => void;
  clearTenant: () => void;
  reset: () => void;
}

export const useSessionStore = create<SessionState>((set, get) => ({
  me: null,
  tenantId: null,
  setMe: (me) => {
    const tenantId = pickInitialTenant(me, readStoredTenant());
    writeStoredTenant(tenantId);
    set({ me, tenantId });
  },
  selectTenant: (tenantId) => {
    const me = get().me;
    if (!me?.memberships.some((m) => m.tenantId === tenantId)) return;
    writeStoredTenant(tenantId);
    set({ tenantId });
  },
  clearTenant: () => {
    writeStoredTenant(null);
    set({ tenantId: null });
  },
  reset: () => {
    writeStoredTenant(null);
    set({ me: null, tenantId: null });
  },
}));

export function useCurrentMembership(): Membership | null {
  return useSessionStore((s) => s.me?.memberships.find((m) => m.tenantId === s.tenantId) ?? null);
}

/** Non-hook accessor for the API client. */
export const getSelectedTenantId = () => useSessionStore.getState().tenantId;
