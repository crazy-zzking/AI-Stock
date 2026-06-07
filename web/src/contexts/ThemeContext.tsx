import React, { createContext, useContext, useEffect, useState } from 'react';

interface ThemeCtx {
  dark: boolean;
  toggle: () => void;
}

const ThemeContext = createContext<ThemeCtx>({ dark: false, toggle: () => {} });

const STORAGE_KEY = 'aistock.theme.dark';

export const ThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [dark, setDark] = useState<boolean>(() => localStorage.getItem(STORAGE_KEY) === '1');

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, dark ? '1' : '0');
  }, [dark]);

  return (
    <ThemeContext.Provider value={{ dark, toggle: () => setDark((d) => !d) }}>
      {children}
    </ThemeContext.Provider>
  );
};

export const useThemeMode = () => useContext(ThemeContext);
