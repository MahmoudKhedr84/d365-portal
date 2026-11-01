import {
  BrowserCacheLocation,
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
  type RedirectRequest,
} from '@azure/msal-browser';
import type { AppConfig } from '@/config';

let instance: PublicClientApplication | null = null;
let apiScopes: string[] = [];

/** MSAL with auth-code + PKCE (the only flow msal-browser v2+ uses for SPAs). */
export function createMsal(config: AppConfig): PublicClientApplication {
  apiScopes = [config.apiScope];
  instance = new PublicClientApplication({
    auth: {
      clientId: config.entraClientId,
      authority: config.entraAuthority,
      redirectUri: window.location.origin,
      postLogoutRedirectUri: window.location.origin,
      navigateToLoginRequestUrl: true,
    },
    cache: {
      // Tokens live only for the browser tab session.
      cacheLocation: BrowserCacheLocation.SessionStorage,
    },
  });
  return instance;
}

export function loginRequest(): RedirectRequest {
  return { scopes: apiScopes, prompt: 'select_account' };
}

function msal(): PublicClientApplication {
  if (!instance) throw new Error('MSAL is not initialized.');
  return instance;
}

export function activeAccount(): AccountInfo | null {
  const app = msal();
  return app.getActiveAccount() ?? app.getAllAccounts()[0] ?? null;
}

/** Access token for the portal API. Falls back to an interactive redirect when consent/MFA is needed. */
export async function getApiToken(): Promise<string> {
  const app = msal();
  const account = activeAccount();
  if (!account) {
    await app.loginRedirect(loginRequest());
    throw new Error('Redirecting to sign in…');
  }
  try {
    const result = await app.acquireTokenSilent({ scopes: apiScopes, account });
    return result.accessToken;
  } catch (e) {
    if (e instanceof InteractionRequiredAuthError) {
      await app.acquireTokenRedirect({ scopes: apiScopes, account });
      throw new Error('Redirecting to sign in…');
    }
    throw e;
  }
}

export async function signOut(): Promise<void> {
  const account = activeAccount();
  sessionStorage.clear();
  await msal().logoutRedirect({ account: account ?? undefined });
}
