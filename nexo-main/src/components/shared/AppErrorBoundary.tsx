import { Component, type ErrorInfo, type ReactNode } from "react";
import { useLocation } from "react-router-dom";
import { ErrorFallback } from "./ErrorFallback";

interface Props {
  children: ReactNode;
  /** When this changes, a thrown error auto-clears (used to reset on navigation). */
  resetKey?: string;
}

interface State {
  hasError: boolean;
}

/**
 * Class boundary — error boundaries must be class components. Catches render
 * errors in the subtree and shows the Orken fallback instead of a white screen.
 */
class ErrorBoundaryInner extends Component<Props, State> {
  state: State = { hasError: false };

  static getDerivedStateFromError(): State {
    return { hasError: true };
  }

  componentDidCatch(error: unknown, info: ErrorInfo) {
    // Never swallow silently. Full detail in dev; a compact, PII-free line in
    // prod (a hook for real telemetry later) — never the stack to the user.
    if (import.meta.env.DEV) {
      console.error("[Orken] Error boundary caught:", error, info.componentStack);
    } else {
      console.error(
        "[Orken] UI error:",
        error instanceof Error ? error.message : String(error)
      );
    }
  }

  componentDidUpdate(prevProps: Props) {
    // Auto-recover when the user navigates to a different route.
    if (this.state.hasError && prevProps.resetKey !== this.props.resetKey) {
      this.setState({ hasError: false });
    }
  }

  private reset = () => this.setState({ hasError: false });

  render() {
    if (this.state.hasError) {
      return <ErrorFallback onRetry={this.reset} />;
    }
    return this.props.children;
  }
}

/**
 * Global app error boundary. Keyed to the current pathname so navigating (or
 * "Voltar ao início") clears the error and renders the destination fresh.
 * Lives inside the router/auth/workspace providers, so those keep working.
 */
export function AppErrorBoundary({ children }: { children: ReactNode }) {
  const { pathname } = useLocation();
  return <ErrorBoundaryInner resetKey={pathname}>{children}</ErrorBoundaryInner>;
}
