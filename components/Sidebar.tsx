import React from 'react';
import type { AppTabKey } from '../types';

interface SidebarProps {
  activeTab: AppTabKey;
  setActiveTab: (tab: AppTabKey) => void;
}

export const Sidebar: React.FC<SidebarProps> = ({ activeTab, setActiveTab }) => {
  const menuItems: Array<{ id: AppTabKey; label: string; icon: string }> = [
    { id: 'orchestrator', label: 'Helper Core', icon: '⚡' },
    { id: 'runtime', label: 'Runtime Console', icon: '🖥️' },
    { id: 'strategy', label: 'Strategic Map', icon: '🧠' },
    { id: 'objectives', label: 'Objectives', icon: '🎯' },
    { id: 'planner', label: 'Architecture', icon: '📐' },
    { id: 'evolution', label: 'Evolution', icon: '🧬' },
    { id: 'builder', label: 'Live Builder', icon: '🛠️' },
    { id: 'indexing', label: 'Library Indexing', icon: '📚' },
    { id: 'settings', label: 'Settings', icon: '⚙️' },
  ];

  return (
    <div className="w-full min-h-0 bg-transparent border-r border-slate-800/80 flex flex-col h-full">
      <div className="p-6 border-b border-slate-800">
        <h1 className="text-xl font-bold text-primary-400 tracking-wider">HELPER</h1>
        <p className="text-[10px] text-slate-500 mt-1 uppercase font-bold tracking-tighter">Helper Generation Runtime</p>
      </div>
      
      <nav className="flex-1 py-4">
        {menuItems.map((item) => (
          <button
            key={item.id}
            onClick={() => setActiveTab(item.id)}
            className={`mx-3 my-1 flex w-[calc(100%-1.5rem)] items-center gap-3 rounded-xl border px-4 py-3 text-left text-sm font-medium transition-all ${
              activeTab === item.id
                ? 'border-blue-500/60 bg-blue-500/20 text-blue-100 shadow-[0_10px_30px_rgba(37,99,235,0.18)]'
                : 'border-blue-950/60 bg-blue-950/25 text-blue-100/85 hover:border-blue-800/80 hover:bg-blue-900/35'
            }`}
          >
            <span className="text-lg">{item.icon}</span>
            {item.label}
          </button>
        ))}
      </nav>

      <div className="p-4 border-t border-slate-800/80">
        <div className="flex items-center gap-2 text-[10px] text-green-500 font-mono">
          <span className="w-1.5 h-1.5 bg-green-500 rounded-full animate-pulse"></span>
          HELPER RUNTIME ACTIVE
        </div>
      </div>
    </div>
  );
};
