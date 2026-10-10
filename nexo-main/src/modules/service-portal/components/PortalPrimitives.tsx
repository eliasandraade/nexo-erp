import { type ButtonHTMLAttributes, type CSSProperties, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/** Heading in the theme's display font. */
export function Display({ children, className, style }: { children: ReactNode; className?: string; style?: CSSProperties }) {
  return (
    <span className={cn("font-bold", className)}
      style={{ fontFamily: "var(--p-display)", ...style }}>
      {children}
    </span>
  );
}

type BtnProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: "solid" | "soft" | "ghost" | "outline";
};

export function Btn({ variant = "solid", className, style, ...props }: BtnProps) {
  const base =
    "inline-flex items-center justify-center gap-2 rounded-[calc(var(--p-radius)*0.7)] " +
    "px-4 py-2.5 text-sm font-semibold transition-colors duration-150 " +
    "disabled:opacity-50 disabled:pointer-events-none focus-visible:outline-none " +
    "focus-visible:ring-2 focus-visible:ring-[color:var(--p-ink)] focus-visible:ring-offset-2";
  const styles: Record<string, CSSProperties> = {
    solid:   { background: "var(--p-accent)", color: "var(--p-accent-ink)" },
    soft:    { background: "var(--p-accent-soft)", color: "var(--p-ink)" },
    ghost:   { background: "transparent", color: "var(--p-ink)" },
    outline: { background: "var(--p-surface)", color: "var(--p-ink)", border: "1px solid var(--p-line)" },
  };
  return <button className={cn(base, className)} style={{ ...styles[variant], ...style }} {...props} />;
}

/** A card surface tinted to the theme. */
export function Surface({ children, className, style, interactive }: {
  children: ReactNode; className?: string; style?: CSSProperties; interactive?: boolean;
}) {
  return (
    <div
      className={cn(
        "rounded-[var(--p-radius)] border",
        interactive && "transition-colors duration-150",
        className,
      )}
      style={{
        background: "var(--p-surface)",
        borderColor: "var(--p-line)",
        ...style,
      }}
    >
      {children}
    </div>
  );
}

/** Initials avatar tinted by the professional's color (no photo field in the backend). */
export function Avatar({ name, color, size = 44 }: { name: string; color?: string | null; size?: number }) {
  const initials = name.split(" ").filter(Boolean).slice(0, 2).map((w) => w[0]?.toUpperCase()).join("");
  const c = color && /^#([0-9a-fA-F]{6})$/.test(color) ? color : "var(--p-accent)";
  return (
    <span
      aria-hidden="true" className="inline-flex shrink-0 items-center justify-center rounded-full font-semibold"
      style={{
        width: size, height: size,
        background: `color-mix(in srgb, ${c} 16%, var(--p-surface))`,
        color: c,
        fontSize: size * 0.38,
        fontFamily: "var(--p-display)",
      }}
    >
      {initials || "•"}
    </span>
  );
}

/** Fades + lifts its children in on mount, with an optional stagger delay. Respects reduced motion. */
export function Reveal({ children, className }: { children: ReactNode; delay?: number; className?: string }) {
  return <div className={className}>{children}</div>;
}

export function Muted({ children, className }: { children: ReactNode; className?: string }) {
  return <span className={className} style={{ color: "var(--p-muted)" }}>{children}</span>;
}
