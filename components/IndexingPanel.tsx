import React, { useState } from 'react';
import { useOperationsRuntime } from '../contexts/OperationsRuntimeContext';
import { challengeEvolutionRound } from '../services/evolutionOperationsApi';

export const IndexingPanel: React.FC = () => {
  const {
    library: books,
    status,
    selectedBookPath,
    setSelectedBookPath,
    error,
    startIndexing,
    pauseIndexing,
    resetIndexing,
  } = useOperationsRuntime();
  const [challengeProposal, setChallengeProposal] = useState<string>('');
  const [roundtableReport, setRoundtableReport] = useState<any>(null);
  const [isChallenging, setIsChallenging] = useState(false);
  const [challengeError, setChallengeError] = useState<string | null>(null);

  const handleChallenge = async () => {
    if (!challengeProposal.trim()) return;
    setIsChallenging(true);
    setChallengeError(null);
    try {
      setRoundtableReport(await challengeEvolutionRound(challengeProposal));
      setChallengeError(null);
    } catch (e) {
      setChallengeError(mapIndexingApiError(e));
    } finally {
      setIsChallenging(false);
    }
  };

  const handleAction = async (action: 'start' | 'pause' | 'reset') => {
    try {
      if (action === 'start') {
        await startIndexing(selectedBookPath || undefined);
      } else if (action === 'pause') {
        await pauseIndexing();
      } else {
        await resetIndexing();
      }
    } catch {
      // Shared operations context exposes the actionable error state.
    }
  };

  const overallProgress = status && status.totalFiles > 0 
    ? Math.round((status.processedFiles / status.totalFiles) * 100) 
    : 0;

  return (
    <div className="p-6 bg-slate-900 rounded-xl border border-slate-800 shadow-xl m-8 max-w-5xl">
      <div className="flex justify-between items-start mb-8">
        <div>
          <h2 className="text-2xl font-bold text-primary-400 flex items-center gap-3">
            📚 Library Indexing Core
          </h2>
          <p className="text-slate-500 text-sm mt-1">Independent Knowledge Ingestion • shared runtime state with Evolution</p>
        </div>
        <div className={`px-3 py-1 rounded-full text-[10px] font-bold uppercase tracking-widest ${status?.isIndexing ? 'bg-blue-900/30 text-blue-500 animate-pulse' : 'bg-slate-800 text-slate-500'}`}>
          {status?.isIndexing ? 'Indexing Active' : 'Indexing Idle'}
        </div>
      </div>

      {/* ... (остальной UI без изменений) ... */}

      {error && (
        <div className="mb-6 p-4 bg-red-900/20 border border-red-800/50 text-red-400 rounded-xl text-sm flex items-center gap-3">
          <span>⚠️</span> {error}
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        <div className="lg:col-span-2 space-y-6">
          {/* Main Progress */}
          <div className="bg-slate-950/50 p-6 rounded-2xl border border-slate-800">
            <div className="flex justify-between items-end mb-4">
              <span className="text-sm font-medium text-slate-400 uppercase tracking-wider">Overall Library Progress</span>
              <span className="text-2xl font-black text-primary-400">{overallProgress}%</span>
            </div>
            <div className="w-full bg-slate-900 rounded-full h-6 p-1 border border-slate-800 shadow-inner">
              <div 
                className="bg-gradient-to-r from-primary-700 via-primary-500 to-primary-400 h-full rounded-full transition-all duration-1000 shadow-lg shadow-blue-500/30"
                style={{ width: `${overallProgress}%` }}
              ></div>
            </div>
            <div className="flex justify-between mt-4 text-[10px] text-slate-500 font-bold uppercase tracking-tighter">
              <span>Books: {status?.processedFiles} / {status?.totalFiles}</span>
              <span>System Phase: {status?.currentPhase}</span>
            </div>
          </div>

          {/* Current File Detail */}
          {status?.isLearning && status.activeTask !== "None" && (
            <div className="bg-primary-950/10 p-6 rounded-2xl border border-primary-900/30 animate-in fade-in slide-in-from-bottom-4">
              <div className="flex justify-between items-center mb-3">
                <span className="text-xs font-bold text-primary-500 uppercase tracking-widest">Processing Now</span>
                <span className="text-sm font-mono text-primary-400">{Math.round(status.fileProgress)}%</span>
              </div>
              <h3 className="text-slate-200 font-medium truncate mb-4" title={status.activeTask}>
                📄 {status.activeTask.split('\\').pop()}
              </h3>
              <div className="w-full bg-slate-900 rounded-full h-2 overflow-hidden">
                <div 
                  className="bg-primary-500 h-full transition-all duration-300"
                  style={{ width: `${status.fileProgress}%` }}
                ></div>
              </div>
            </div>
          )}

          {/* Controls */}
          <div className="flex gap-4">
            <button 
              onClick={() => handleAction('start')}
              className={`py-4 rounded-2xl font-bold transition-all flex items-center justify-center gap-3 shadow-xl ${status?.isLearning ? 'bg-slate-800 text-slate-400 cursor-not-allowed' : 'bg-primary-600 hover:bg-primary-500 text-white transform active:scale-95 shadow-primary-900/20'}`}
              style={{ flex: 2 }}
              disabled={status?.isLearning}
            >
              <span className="text-xl">▶</span> Start Learning
            </button>
            <button 
              onClick={() => handleAction('pause')}
              className="flex-1 bg-slate-800 hover:bg-slate-700 text-white py-4 rounded-2xl font-bold transition-all flex items-center justify-center gap-2 shadow-lg"
            >
              <span className="text-xl">⏸</span> Pause
            </button>
            <button 
              onClick={() => { if(window.confirm('Reset queue?')) handleAction('reset'); }}
              className="flex-1 border border-red-900/30 hover:bg-red-900/10 text-red-500 py-4 rounded-2xl font-bold transition-all flex items-center justify-center gap-2"
            >
              <span className="text-xl">🔄</span> Reset
            </button>
          </div>
        </div>

        {/* Sidebar: Library List */}
        <div className="bg-slate-950/30 rounded-2xl border border-slate-800 flex flex-col" style={{ height: '500px' }}>
          <div className="p-4 border-b border-slate-800">
            <h3 className="text-xs font-black text-slate-500 uppercase tracking-widest">Local Library</h3>
          </div>
          <div className="flex-1 overflow-y-auto p-2 space-y-1 custom-scrollbar">
            {books.map((book, idx) => (
              <div 
                key={idx}
                onClick={() => setSelectedBookPath(book.path)}
                className={`p-3 rounded-xl text-xs cursor-pointer transition-all border ${selectedBookPath === book.path ? 'bg-primary-900/20 border-primary-800/50 text-primary-200' : 'bg-transparent border-transparent hover:bg-slate-800/50 text-slate-400'}`}
              >
                <div className="flex justify-between items-center mb-1">
                  <span className={`text-[9px] font-bold px-1.5 py-0.5 rounded uppercase ${book.status === 'Done' ? 'bg-green-900/30 text-green-500' : book.status === 'Processing' ? 'bg-blue-900/30 text-blue-400 animate-pulse' : 'bg-slate-800 text-slate-500'}`}>
                    {book.status}
                  </span>
                  <span className="text-[10px] text-slate-600 font-mono">{book.folder}</span>
                </div>
                <div className="truncate font-medium">{book.name}</div>
              </div>
            ))}
          </div>
          {selectedBookPath && (
            <div className="p-4 border-t border-slate-800 bg-primary-950/10">
              <button 
                onClick={() => handleAction('start')}
                className="w-full bg-primary-600 hover:bg-primary-500 text-white py-2 rounded-lg text-[10px] font-black uppercase tracking-widest transition-all"
              >
                Index Selected Only
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Shadow Roundtable Section */}
      <div className="mt-12 pt-12 border-t border-slate-800">
        <h2 className="text-2xl font-bold text-primary-400 mb-2 flex items-center gap-3">
          🎭 Shadow Roundtable
        </h2>
        <p className="text-slate-500 text-sm mb-6">Persona-based critical analysis engine (PSM Implementation)</p>

        <div className="bg-slate-950/50 p-6 rounded-2xl border border-slate-800 mb-8">
          <label className="block text-xs font-bold text-slate-500 uppercase tracking-widest mb-3">Propose Architecture / Idea</label>
          <div className="flex gap-4">
            <input 
              type="text" 
              value={challengeProposal}
              onChange={(e) => setChallengeProposal(e.target.value)}
              placeholder="e.g. Use a decentralized swarm of agents for code refactoring..."
              className="flex-1 bg-slate-900 border border-slate-800 rounded-xl px-4 py-3 text-slate-200 outline-none focus:ring-2 focus:ring-primary-500"
            />
            <button 
              onClick={handleChallenge}
              disabled={isChallenging || !challengeProposal}
              className="bg-primary-600 hover:bg-primary-500 disabled:opacity-30 text-white px-8 py-3 rounded-xl font-bold transition-all"
            >
              {isChallenging ? 'Debating...' : 'Challenge Me'}
            </button>
          </div>
          {challengeError && (
            <div className="mt-4 rounded-xl border border-amber-800/50 bg-amber-950/20 px-4 py-3 text-sm text-amber-200">
              Challenge failed: {challengeError}
            </div>
          )}
        </div>

        {roundtableReport && (
          <div className="space-y-6 animate-in fade-in zoom-in-95 duration-500">
            {/* Persona Opinions */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              {roundtableReport.opinions.map((op: any, idx: number) => (
                <div key={idx} className="bg-slate-900/80 p-5 rounded-2xl border border-slate-800 flex flex-col h-full">
                  <div className="flex justify-between items-center mb-4">
                    <span className={`text-[10px] font-black px-2 py-0.5 rounded uppercase tracking-tighter ${
                      op.persona === 0 ? 'bg-red-900/30 text-red-400' : 
                      op.persona === 1 ? 'bg-green-900/30 text-green-400' : 'bg-blue-900/30 text-blue-400'
                    }`}>
                      {op.persona === 0 ? 'Cynic' : op.persona === 1 ? 'Emergent' : 'Historian'}
                    </span>
                    <span className="text-xs font-mono text-slate-600">Score: {op.criticalScore}</span>
                  </div>
                  <p className="text-sm text-slate-300 italic mb-4 flex-1">"{op.opinion}"</p>
                  <div className="mt-auto pt-4 border-t border-slate-800/50">
                    <span className="text-[9px] font-bold text-slate-500 uppercase block mb-1">Alternative Path</span>
                    <p className="text-[11px] text-primary-400 font-medium">{op.alternativeProposal}</p>
                  </div>
                </div>
              ))}
            </div>

            {/* Synthesized Advice */}
            <div className="bg-primary-600/10 border border-primary-500/30 p-8 rounded-3xl relative overflow-hidden">
              <div className="absolute top-0 right-0 p-4 opacity-10">
                <span className="text-8xl font-black text-primary-500 uppercase rotate-12 select-none">Synthesized</span>
              </div>
              <h3 className="text-xl font-black text-primary-400 mb-4 flex items-center gap-2">
                ⚖️ Final Strategic Synthesis
              </h3>
              <p className="text-lg text-slate-200 leading-relaxed relative z-10">
                {roundtableReport.synthesizedAdvice}
              </p>
              <div className="mt-6 flex items-center gap-4">
                <div className="text-xs font-bold text-slate-500 uppercase">System Conflict Level</div>
                <div className="flex-1 h-1.5 bg-slate-800 rounded-full overflow-hidden">
                  <div className="h-full bg-primary-500" style={{ width: `${roundtableReport.conflictLevel * 100}%` }}></div>
                </div>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

function mapIndexingApiError(error: unknown): string {
  if (!(error instanceof Error)) {
    return 'Connection to Helper API failed.';
  }

  const message = error.message || '';
  if (message.includes('API 403')) {
    return message.toLowerCase().includes('missing scope')
      ? 'Helper API session is missing indexing scopes (403). Session bootstrap was retried; reload if the error persists.'
      : 'Helper API denied access to indexing endpoints (403). Check session bootstrap scopes and backend auth policy.';
  }

  if (message.includes('API 401')) {
    return 'Helper API authentication failed (401). Check HELPER_API_KEY/session bootstrap.';
  }

  if (message.toLowerCase().includes('network error') || message.includes('Failed to fetch')) {
    return 'Connection to Helper API failed (network). Check backend status and API port.';
  }

  return `Helper API error: ${message}`;
}
