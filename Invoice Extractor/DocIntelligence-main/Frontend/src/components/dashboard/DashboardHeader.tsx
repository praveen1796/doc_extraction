import { motion } from "framer-motion";
import { useState } from "react";
import { useNavigate } from "react-router-dom";

export function DashboardHeader() {
  const navigate = useNavigate();
  const [showQuickActions, setShowQuickActions] = useState(false);
  const now = new Date();
  const utcTime = now.toISOString().slice(11, 19) + " UTC";

  const actions = [
    {
      label: "Upload",
      primary: true,
      onClick: () => navigate("/upload"),
      icon: (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4" />
          <polyline points="17 8 12 3 7 8" />
          <line x1="12" y1="3" x2="12" y2="15" />
        </svg>
      ),
    },
    {
      label: "Ask AI",
      icon: (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <path d="M12 2a8.5 8.5 0 00-8.5 8.5c0 3.03 1.6 5.7 4 7.2V20a2 2 0 002 2h5a2 2 0 002-2v-2.3c2.4-1.5 4-4.17 4-7.2A8.5 8.5 0 0012 2z" />
        </svg>
      ),
    },
    {
      label: "Validate",
      icon: (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
          <polyline points="9 12 11 14 15 10" />
        </svg>
      ),
    },
    {
      label: "Compare",
      icon: (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <circle cx="18" cy="18" r="3" /><circle cx="6" cy="6" r="3" />
          <path d="M6 21V9a9 9 0 009 9" />
        </svg>
      ),
    },
    {
      label: "Studio",
      onClick: () => navigate("/studio"),
      icon: (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z" />
        </svg>
      ),
    },
  ];

  const quickActions = [
    {
      label: "Find anomalies",
      icon: (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" />
        </svg>
      ),
    },
    {
      label: "Check duplicates",
      icon: (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <rect x="9" y="9" width="13" height="13" rx="2" /><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1" />
        </svg>
      ),
    },
    {
      label: "Generate summary",
      icon: (
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
          <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
          <polyline points="14 2 14 8 20 8" />
          <line x1="16" y1="13" x2="8" y2="13" /><line x1="16" y1="17" x2="8" y2="17" />
        </svg>
      ),
    },
  ];

  return (
    <motion.header
      initial={{ opacity: 0, y: -12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.5 }}
      className="flex items-center justify-between"
    >
      <div className="flex items-center gap-4">
        <div className="flex items-center gap-3">
          <div className="h-10 w-10 rounded-xl bg-gradient-to-br from-primary to-cyan-300 flex items-center justify-center shadow-lg shadow-primary/20">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="white" strokeWidth="2" strokeLinecap="round">
              <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
              <polyline points="14 2 14 8 20 8" />
              <line x1="9" y1="13" x2="15" y2="13" /><line x1="9" y1="17" x2="13" y2="17" />
            </svg>
          </div>
          <div>
            <h1 className="text-xl font-bold text-foreground tracking-tight">DocIQ</h1>
            <p className="text-xs text-muted-foreground -mt-0.5">Command Center</p>
          </div>
        </div>
        <div className="hidden sm:flex items-center gap-2 ml-4 rounded-full border border-accent/30 bg-accent/5 px-3.5 py-1.5">
          <span className="status-dot-live" />
          <span className="text-xs font-semibold text-accent">Pipeline Live</span>
        </div>
      </div>

      <div className="flex items-center gap-2">
        <div className="hidden md:flex items-center gap-1.5 text-xs text-muted-foreground mr-3">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
            <circle cx="12" cy="12" r="10" /><polyline points="12 6 12 12 16 14" />
          </svg>
          {utcTime}
        </div>
        {actions.map(({ icon, label, primary, onClick }) => (
          <button
            key={label}
            onClick={onClick}
            className={`hidden sm:inline-flex items-center gap-2 rounded-lg border px-4 py-2.5 text-xs font-medium transition-all duration-200 ${
              primary
                ? "border-primary/40 bg-primary/10 text-primary hover:bg-primary/20 btn-glow"
                : "border-border bg-secondary/50 text-secondary-foreground btn-glow hover:bg-primary/10 hover:text-primary"
            }`}
          >
            {icon}
            {label}
          </button>
        ))}
        <div className="relative">
          <button
            onClick={() => setShowQuickActions(!showQuickActions)}
            className="inline-flex items-center gap-1.5 rounded-lg border border-primary/30 bg-primary/10 px-3.5 py-2.5 text-xs font-medium text-primary btn-glow hover:bg-primary/20 transition-all duration-200"
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2" />
            </svg>
            <span className="hidden sm:inline">Quick</span>
            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round">
              <polyline points="6 9 12 15 18 9" />
            </svg>
          </button>
          {showQuickActions && (
            <>
              <div className="fixed inset-0 z-40" onClick={() => setShowQuickActions(false)} />
              <motion.div
                initial={{ opacity: 0, y: 4, scale: 0.97 }}
                animate={{ opacity: 1, y: 0, scale: 1 }}
                className="absolute right-0 top-full mt-2 z-50 glass-panel p-1.5 w-52"
              >
                {quickActions.map(({ icon, label }) => (
                  <button
                    key={label}
                    className="flex w-full items-center gap-2.5 rounded-lg px-3 py-2.5 text-xs font-medium text-secondary-foreground hover:bg-primary/10 hover:text-primary transition-colors"
                    onClick={() => setShowQuickActions(false)}
                  >
                    <span className="text-muted-foreground">{icon}</span>
                    {label}
                  </button>
                ))}
              </motion.div>
            </>
          )}
        </div>
      </div>
    </motion.header>
  );
}
