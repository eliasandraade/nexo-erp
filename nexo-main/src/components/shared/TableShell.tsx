import { cn } from "@/lib/utils";

interface TableShellProps {
  children: React.ReactNode;
  className?: string;
}

export function TableShell({ children, className }: TableShellProps) {
  return (
    <div className={cn("w-full overflow-x-auto", className)}>
      <table className="w-full text-[12.5px] border-collapse">
        {children}
      </table>
    </div>
  );
}

interface ThProps extends React.ThHTMLAttributes<HTMLTableCellElement> {
  children?: React.ReactNode;
}

export function Th({ children, className, ...props }: ThProps) {
  return (
    <th
      className={cn(
        "px-3 py-2 text-left text-[11px] font-semibold uppercase tracking-[0.07em] text-muted-foreground border-b border-border bg-muted/40 whitespace-nowrap",
        className
      )}
      {...props}
    >
      {children}
    </th>
  );
}

interface TdProps extends React.TdHTMLAttributes<HTMLTableCellElement> {
  children?: React.ReactNode;
}

export function Td({ children, className, ...props }: TdProps) {
  return (
    <td
      className={cn(
        "px-3 py-2.5 text-foreground border-b border-border/60 align-middle",
        className
      )}
      {...props}
    >
      {children}
    </td>
  );
}

interface TrProps extends React.HTMLAttributes<HTMLTableRowElement> {
  children?: React.ReactNode;
}

export function Tr({ children, className, ...props }: TrProps) {
  return (
    <tr
      className={cn(
        "hover:bg-muted/30 transition-colors duration-100",
        className
      )}
      {...props}
    >
      {children}
    </tr>
  );
}
