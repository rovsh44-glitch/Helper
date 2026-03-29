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
    <div className="w-64 bg-slate-900 border-r border-slate-800 flex flex-col h-full shadow-2xl">
      <div className="p-6 border-b border-slate-800">
        <h1 className="text-xl font-bold text-primary-400 tracking-wider">HELPER</h1>
        <p className="text-[10px] text-slate-500 mt-1 uppercase font-bold tracking-tighter">Helper Generation Runtime</p>
      </div>
      
      <nav className="flex-1 py-4">
        {menuItems.map((item) => (
          <button
            key={item.id}
            onClick={() => setActiveTab(item.id)}
            className={`w-full text-left px-6 py-3 text-sm font-medium transition-all flex items-center gap-3 ${
              activeTab === item.id
                ? 'bg-primary-900/20 text-primary-400 border-r-4 border-primary-500'
                : 'text-slate-400 hover:bg-slate-800/50 hover:text-slate-200'
            }`}
          >
            <span className="text-lg">{item.icon}</span>
            {item.label}
          </button>
        ))}
      </nav>

      <div className="p-4 border-t border-slate-800 bg-black/20">
        <div className="flex items-center gap-2 text-[10px] text-green-500 font-mono">
          <span className="w-1.5 h-1.5 bg-green-500 rounded-full animate-pulse"></span>
          HELPER RUNTIME ACTIVE
        </div>
      </div>
    </div>
  );
};
