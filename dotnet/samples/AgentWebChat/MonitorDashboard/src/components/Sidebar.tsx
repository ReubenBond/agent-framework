import { NavLink } from 'react-router-dom';
import './Sidebar.css';

interface SidebarProps {
  isConnected: boolean;
  isLightTheme: boolean;
  onToggleTheme: () => void;
  onReconnect: () => void;
}

// Theme toggle icons
function SunIcon() {
  return (
    <svg className="theme-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
      <circle cx="12" cy="12" r="5" />
      <line x1="12" y1="1" x2="12" y2="3" />
      <line x1="12" y1="21" x2="12" y2="23" />
      <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
      <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
      <line x1="1" y1="12" x2="3" y2="12" />
      <line x1="21" y1="12" x2="23" y2="12" />
      <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
      <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
    </svg>
  );
}

function MoonIcon() {
  return (
    <svg className="theme-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
      <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
    </svg>
  );
}

export function Sidebar({ isConnected, isLightTheme, onToggleTheme, onReconnect }: SidebarProps) {
  return (
    <nav className="sidebar">
      <div className="sidebar-header">
        <h1 className="app-title">Maru</h1>
        <span className="app-subtitle">Microsoft Agent Runtime</span>
      </div>

      <div className="sidebar-nav">
        <NavLink to="/" end className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
          <svg className="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <rect x="3" y="3" width="7" height="7" rx="1" />
            <rect x="14" y="3" width="7" height="7" rx="1" />
            <rect x="3" y="14" width="7" height="7" rx="1" />
            <rect x="14" y="14" width="7" height="7" rx="1" />
          </svg>
          <span>Dashboard</span>
        </NavLink>

        <NavLink to="/workers" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
          <svg className="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <rect x="2" y="6" width="20" height="12" rx="2" />
            <line x1="6" y1="10" x2="6" y2="14" />
            <line x1="10" y1="10" x2="10" y2="14" />
            <line x1="14" y1="10" x2="14" y2="14" />
          </svg>
          <span>Workers</span>
        </NavLink>

        <NavLink to="/workflows" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
          <svg className="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            <circle cx="5" cy="6" r="3" />
            <circle cx="19" cy="6" r="3" />
            <circle cx="12" cy="18" r="3" />
            <line x1="5" y1="9" x2="12" y2="15" />
            <line x1="19" y1="9" x2="12" y2="15" />
          </svg>
          <span>Workflows</span>
        </NavLink>
      </div>

      <div className="sidebar-footer">
        <div className="footer-row">
          {isConnected ? (
            <div className="connection-indicator">
              <span className="status-dot connected" />
              <span>Live</span>
            </div>
          ) : (
            <button className="reconnect-button" onClick={onReconnect}>
              <span className="status-dot disconnected" />
              <span>Reconnect</span>
            </button>
          )}
          <button 
            className="theme-toggle" 
            onClick={onToggleTheme} 
            title={`Switch to ${isLightTheme ? 'dark' : 'light'} theme (T)`}
          >
            {isLightTheme ? <MoonIcon /> : <SunIcon />}
          </button>
        </div>
      </div>
    </nav>
  );
}
