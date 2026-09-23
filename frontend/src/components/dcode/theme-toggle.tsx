"use client";

import { useEffect, useState } from "react";
import { Moon, Sun } from "lucide-react";
import { Button } from "@dbs-studio/ui";

export function ThemeToggle() {
  const [dark, setDark] = useState(true);

  useEffect(() => {
    const isDark = document.documentElement.classList.contains("dark");
    setDark(isDark);
  }, []);

  function toggleTheme() {
    const nextDark = !dark;

    document.documentElement.classList.toggle("dark", nextDark);
    localStorage.setItem("dcode-theme", nextDark ? "dark" : "light");

    setDark(nextDark);
  }

  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={toggleTheme}
      aria-label={`Switch to ${dark ? "light" : "dark"} theme`}
    >
      {dark ? <Sun size={18} /> : <Moon size={18} />}
    </Button>
  );
}