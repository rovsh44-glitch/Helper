import React, {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useReducer,
  type Dispatch,
  type ReactNode,
  type SetStateAction,
} from 'react';
import type { ChatAttachment, LiveWebMode, Message } from '../types';

export type StartupState = 'booting' | 'ready' | 'degraded';

type ConversationRuntimeState = {
  messages: Message[];
  input: string;
  isProcessing: boolean;
  conversationId?: string;
  activeBranchId: string;
  availableBranches: string[];
  pendingAttachments: ChatAttachment[];
  streamingMessageId?: string;
  sessionEpoch: number;
};

type ConversationShellState = {
  startupState: StartupState;
  startupAlert: string | null;
  responseStyle: string;
  resumeAvailable: boolean;
  liveWebMode: LiveWebMode;
};

type ConversationState = {
  runtime: ConversationRuntimeState;
  shell: ConversationShellState;
};

type ConversationSnapshotState = {
  conversationId: string;
  activeBranchId: string;
  availableBranches: string[];
  messages: Message[];
};

type ConversationAction =
  | { type: 'set_messages'; value: SetStateAction<Message[]> }
  | { type: 'set_input'; value: string }
  | { type: 'set_processing'; value: boolean }
  | { type: 'set_conversation_id'; value?: string }
  | { type: 'set_active_branch'; value: string }
  | { type: 'set_available_branches'; value: SetStateAction<string[]> }
  | { type: 'set_pending_attachments'; value: ChatAttachment[] }
  | { type: 'set_streaming_message'; value?: string }
  | { type: 'apply_snapshot'; value: ConversationSnapshotState }
  | { type: 'reset_runtime_surface' }
  | { type: 'set_startup'; value: { state: StartupState; alert: string | null } }
  | { type: 'set_response_style'; value: string }
  | { type: 'set_resume_available'; value: boolean }
  | { type: 'set_live_web_mode'; value: LiveWebMode };

const initialState: ConversationState = {
  runtime: {
    messages: [],
    input: '',
    isProcessing: false,
    conversationId: undefined,
    activeBranchId: 'main',
    availableBranches: ['main'],
    pendingAttachments: [],
    streamingMessageId: undefined,
    sessionEpoch: 0,
  },
  shell: {
    startupState: 'booting',
    startupAlert: null,
    responseStyle: 'balanced',
    resumeAvailable: false,
    liveWebMode: 'auto',
  },
};

const ConversationRuntimeStateContext = createContext<ConversationRuntimeState | null>(null);
const ConversationShellStateContext = createContext<ConversationShellState | null>(null);
const ConversationDispatchContext = createContext<Dispatch<ConversationAction> | null>(null);

function reducer(state: ConversationState, action: ConversationAction): ConversationState {
  switch (action.type) {
    case 'set_messages': {
      const nextMessages = typeof action.value === 'function'
        ? action.value(state.runtime.messages)
        : action.value;
      return {
        ...state,
        runtime: {
          ...state.runtime,
          messages: nextMessages,
        },
      };
    }
    case 'set_input':
      return {
        ...state,
        runtime: {
          ...state.runtime,
          input: action.value,
        },
      };
    case 'set_processing':
      return {
        ...state,
        runtime: {
          ...state.runtime,
          isProcessing: action.value,
        },
      };
    case 'set_conversation_id':
      return {
        ...state,
        runtime: {
          ...state.runtime,
          conversationId: action.value,
        },
      };
    case 'set_active_branch':
      return {
        ...state,
        runtime: {
          ...state.runtime,
          activeBranchId: action.value,
        },
      };
    case 'set_available_branches':
      {
      const nextBranches = typeof action.value === 'function'
        ? action.value(state.runtime.availableBranches)
        : action.value;
      return {
        ...state,
        runtime: {
          ...state.runtime,
          availableBranches: nextBranches.length > 0 ? nextBranches : ['main'],
        },
      };
      }
    case 'set_pending_attachments':
      return {
        ...state,
        runtime: {
          ...state.runtime,
          pendingAttachments: action.value,
        },
      };
    case 'set_streaming_message':
      return {
        ...state,
        runtime: {
          ...state.runtime,
          streamingMessageId: action.value,
        },
      };
    case 'apply_snapshot':
      return {
        ...state,
        runtime: {
          ...state.runtime,
          conversationId: action.value.conversationId,
          activeBranchId: action.value.activeBranchId,
          availableBranches: action.value.availableBranches,
          messages: action.value.messages,
          streamingMessageId: undefined,
        },
      };
    case 'reset_runtime_surface':
      return {
        ...state,
        runtime: {
          ...initialState.runtime,
          sessionEpoch: state.runtime.sessionEpoch + 1,
        },
      };
    case 'set_startup':
      return {
        ...state,
        shell: {
          ...state.shell,
          startupState: action.value.state,
          startupAlert: action.value.alert,
        },
      };
    case 'set_response_style':
      return {
        ...state,
        shell: {
          ...state.shell,
          responseStyle: action.value,
        },
      };
    case 'set_resume_available':
      return {
        ...state,
        shell: {
          ...state.shell,
          resumeAvailable: action.value,
        },
      };
    case 'set_live_web_mode':
      return {
        ...state,
        shell: {
          ...state.shell,
          liveWebMode: action.value,
        },
      };
    default:
      return state;
  }
}

export function ConversationStateProvider({ children, initialResponseStyle }: { children: ReactNode; initialResponseStyle: string }) {
  const [state, dispatch] = useReducer(reducer, {
    ...initialState,
    shell: {
      ...initialState.shell,
      responseStyle: initialResponseStyle,
    },
  });

  const runtimeValue = useMemo(() => state.runtime, [state.runtime]);
  const shellValue = useMemo(() => state.shell, [state.shell]);

  return (
    <ConversationDispatchContext.Provider value={dispatch}>
      <ConversationShellStateContext.Provider value={shellValue}>
        <ConversationRuntimeStateContext.Provider value={runtimeValue}>
          {children}
        </ConversationRuntimeStateContext.Provider>
      </ConversationShellStateContext.Provider>
    </ConversationDispatchContext.Provider>
  );
}

export function useConversationRuntimeState() {
  const context = useContext(ConversationRuntimeStateContext);
  if (!context) {
    throw new Error('useConversationRuntimeState must be used inside ConversationStateProvider.');
  }

  return context;
}

export function useConversationShellState() {
  const context = useContext(ConversationShellStateContext);
  if (!context) {
    throw new Error('useConversationShellState must be used inside ConversationStateProvider.');
  }

  return context;
}

function useConversationDispatch() {
  const context = useContext(ConversationDispatchContext);
  if (!context) {
    throw new Error('useConversationDispatch must be used inside ConversationStateProvider.');
  }

  return context;
}

export function useConversationActions() {
  const dispatch = useConversationDispatch();

  const setMessages = useCallback((value: SetStateAction<Message[]>) => {
    dispatch({ type: 'set_messages', value });
  }, [dispatch]);

  return {
    setMessages,
    setInput: useCallback((value: string) => dispatch({ type: 'set_input', value }), [dispatch]),
    setProcessing: useCallback((value: boolean) => dispatch({ type: 'set_processing', value }), [dispatch]),
    setConversationId: useCallback((value?: string) => dispatch({ type: 'set_conversation_id', value }), [dispatch]),
    setActiveBranchId: useCallback((value: string) => dispatch({ type: 'set_active_branch', value }), [dispatch]),
    setAvailableBranches: useCallback((value: SetStateAction<string[]>) => dispatch({ type: 'set_available_branches', value }), [dispatch]),
    setPendingAttachments: useCallback((value: ChatAttachment[]) => dispatch({ type: 'set_pending_attachments', value }), [dispatch]),
    setStreamingMessageId: useCallback((value?: string) => dispatch({ type: 'set_streaming_message', value }), [dispatch]),
    applySnapshot: useCallback((value: ConversationSnapshotState) => dispatch({ type: 'apply_snapshot', value }), [dispatch]),
    resetConversationRuntime: useCallback(() => dispatch({ type: 'reset_runtime_surface' }), [dispatch]),
    setStartupState: useCallback((stateValue: StartupState, alert: string | null) => dispatch({
      type: 'set_startup',
      value: { state: stateValue, alert },
    }), [dispatch]),
    setResponseStyle: useCallback((value: string) => dispatch({ type: 'set_response_style', value }), [dispatch]),
    setResumeAvailable: useCallback((value: boolean) => dispatch({ type: 'set_resume_available', value }), [dispatch]),
    setLiveWebMode: useCallback((value: LiveWebMode) => dispatch({ type: 'set_live_web_mode', value }), [dispatch]),
  };
}
