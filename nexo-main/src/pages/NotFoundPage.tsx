import { useLocation, Link } from "react-router-dom";
import { useEffect } from "react";

export default function NotFoundPage() {
  const location = useLocation();

  useEffect(() => {
    console.error("404 Error: User attempted to access non-existent route:", location.pathname);
  }, [location.pathname]);

  return (
    <div className="flex min-h-screen items-center justify-center bg-background">
      <div className="text-center">
        <h1 className="mb-4 text-4xl font-bold text-foreground">404</h1>
        <p className="mb-4 text-xl text-muted-foreground">
          Página não encontrada
        </p>
        <Link
          to="/dashboard"
          className="text-secondary underline hover:text-secondary/90"
        >
          Voltar ao Dashboard
        </Link>
      </div>
    </div>
  );
}
