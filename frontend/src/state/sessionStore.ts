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

/** The company to open in a tenant: the saved current company, else the default, else the first allowed one. */
export function pickCompany(m: Membership | null | undefined): string | null {
  if (!m) return null;
  const has = (c: string | null) => !!c && m.companies.some((x) => x.toUpperCase() === c.toUpperCase());
  if (has(m.currentCompany)) return m.currentCompany!.toUpperCase();
  if (has(m.defaultCompany)) return m.defaultCompany!.toUpperCase();
  return m.companies[0]?.toUpperCase() ?? null;
}

const membershipOf = (me: Me | null, tenantId: string | null) => me?.memberships.find((m) => m.tenantId === tenantId) ?? null;

interface SessionState {
  me: Me | null;
  tenantId: string | null;
  /** Working company (dataAreaId) inside the selected tenant; sent as X-Company. */
  company: string | null;
  setMe: (me: Me) => void;
  selectTenant: (tenantId: string) => void;
  /** Switches company within the current tenant (only among the user's allowed companies). */
  selectCompany: (company: string) => boolean;
  clearTenant: () => void;
  reset: () => void;
}

export const useSessionStore = create<SessionState>((set, get) => ({
  me: null,
  tenantId: null,
  company: null,
  setMe: (me) => {
    const tenantId = pickInitialTenant(me, readStoredTenant());
    writeStoredTenant(tenantId);
    set({ me, tenantId, company: pickCompany(membershipOf(me, tenantId)) });
  },
  selectTenant: (tenantId) => {
    const me = get().me;
    if (!me?.memberships.some((m) => m.tenantId === tenantId)) return;
    writeStoredTenant(tenantId);
    set({ tenantId, company: pickCompany(membershipOf(me, tenantId)) });
  },
  selectCompany: (company) => {
    const { me, tenantId } = get();
    const m = membershipOf(me, tenantId);
    const code = company.toUpperCase();
    if (!me || !m || !m.companies.some((c) => c.toUpperCase() === code)) return false;
    set({
      company: code,
      me: {
        ...me,
        memberships: me.memberships.map((x) => (x.tenantId === tenantId ? { ...x, currentCompany: code } : x)),
      },
    });
    return true;
  },
  clearTenant: () => {
    writeStoredTenant(null);
    set({ tenantId: null, company: null });
  },
  reset: () => {
    writeStoredTenant(null);
    set({ me: null, tenantId: null, company: null });
  },
}));

export function useCurrentMembership(): Membership | null {
  return useSessionStore((s) => s.me?.memberships.find((m) => m.tenantId === s.tenantId) ?? null);
}

/** Non-hook accessor for the API client. */
export const getSelectedTenantId = () => useSessionStore.getState().tenantId;
/** Non-hook accessor for the API client. */
export const getSelectedCompany = () => useSessionStore.getState().company;
