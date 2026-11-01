import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { EventType, type AuthenticationResult } from '@azure/msal-browser';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import './i18n';
import './styles.css';
import { loadConfig } from './config';
import { createMsal } from './auth/msal';
import { App } from './App';
import { ConfigErrorPage } from './pages/ConfigErrorPage';

const root = createRoot(document.getElementById('root')!);

async function bootstrap() {
  const result = loadConfig();
  if (!result.ok) {
    root.render(
      <FluentProvider theme={webLightTheme}>
        <ConfigErrorPage errors={result.errors} />
      </FluentProvider>,
    );
    return;
  }

  const msal = createMsal(result.config);
  await msal.initialize();

  // Complete a login redirect (if this page load is the return from Microsoft) and remember the account.
  const redirect = await msal.handleRedirectPromise();
  if (redirect?.account) msal.setActiveAccount(redirect.account);
  else if (!msal.getActiveAccount() && msal.getAllAccounts().length > 0) msal.setActiveAccount(msal.getAllAccounts()[0]!);

  msal.addEventCallback((event) => {
    if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
      msal.setActiveAccount((event.payload as AuthenticationResult).account);
    }
  });

  root.render(
    <StrictMode>
      <App msal={msal} config={result.config} />
    </StrictMode>,
  );
}

void bootstrap();
