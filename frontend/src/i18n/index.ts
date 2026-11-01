import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';

/**
 * English only for now. To add Arabic: create locales/ar.json with the same keys,
 * add it to `resources`, and call setLanguage('ar') — direction switches to RTL
 * (Fluent UI follows via FluentProvider dir in App.tsx).
 */
export const RTL_LANGUAGES = new Set(['ar', 'he', 'fa', 'ur']);

export const directionFor = (lng: string): 'ltr' | 'rtl' =>
  RTL_LANGUAGES.has(lng.split('-')[0] ?? lng) ? 'rtl' : 'ltr';

void i18n.use(initReactI18next).init({
  resources: { en: { translation: en } },
  lng: 'en',
  fallbackLng: 'en',
  interpolation: { escapeValue: false }, // React escapes already
  returnNull: false,
});

function applyDocumentDirection(lng: string) {
  if (typeof document === 'undefined') return;
  document.documentElement.lang = lng;
  document.documentElement.dir = directionFor(lng);
}

applyDocumentDirection(i18n.language);
i18n.on('languageChanged', applyDocumentDirection);

export async function setLanguage(lng: string) {
  await i18n.changeLanguage(lng);
}

export default i18n;
