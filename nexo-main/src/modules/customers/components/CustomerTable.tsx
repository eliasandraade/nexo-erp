import { Link } from "react-router-dom";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { StatusBadge } from "@/components/shared/StatusBadge";
import type { CustomerDto } from "../types";
import { parseAddress } from "../types";
export function CustomerTable({ customers }: { customers: CustomerDto[] }) {
  return <>
    <div className="hidden md:block"><Table>
      <TableHeader><TableRow>
        <TableHead>Nome / Razão social</TableHead><TableHead>Documento</TableHead><TableHead>Contato</TableHead><TableHead>Cidade</TableHead><TableHead>Situação</TableHead><TableHead><span className="sr-only">Ações</span></TableHead>
      </TableRow></TableHeader>
      <TableBody>{customers.map(c => {
        const addr = parseAddress(c.addressJson);
        return <TableRow key={c.id}>
          <TableCell className="max-w-xs"><Link className="font-semibold hover:underline break-words" to={"/clientes/" + c.id}>{c.name}</Link><p className="mt-1 text-xs text-muted-foreground">{c.personType === "Individual" ? "Pessoa física" : "Pessoa jurídica"}</p></TableCell>
          <TableCell className="whitespace-nowrap tabular-nums">{c.documentNumber || "Não informado"}</TableCell>
          <TableCell className="max-w-xs break-words">{c.phone || "Sem telefone"}{c.email && <p className="mt-1 text-xs text-muted-foreground break-all">{c.email}</p>}</TableCell>
          <TableCell>{addr.city ? addr.city + (addr.state ? " / " + addr.state : "") : "Não informada"}</TableCell>
          <TableCell><StatusBadge label={c.isActive ? "Ativo" : "Inativo"} variant={c.isActive ? "success" : "neutral"} dot /></TableCell>
          <TableCell><Link className="inline-flex min-h-10 items-center text-sm text-primary hover:underline" to={"/clientes/" + c.id} aria-label={"Editar " + c.name}>Editar</Link></TableCell>
        </TableRow>;
      })}</TableBody>
    </Table></div>
    <ul className="record-list md:hidden">{customers.map(c => {
      const addr = parseAddress(c.addressJson);
      return <li key={c.id} className="py-4">
        <div className="flex items-start justify-between gap-3"><Link to={"/clientes/" + c.id} className="min-w-0 break-words font-semibold text-primary underline-offset-4 hover:underline">{c.name}</Link><StatusBadge label={c.isActive ? "Ativo" : "Inativo"} variant={c.isActive ? "success" : "neutral"} /></div>
        <dl className="mt-3 grid grid-cols-[5rem_minmax(0,1fr)] gap-x-3 gap-y-2 text-sm">
          <dt className="text-muted-foreground">Tipo</dt><dd>{c.personType === "Individual" ? "Pessoa física" : "Pessoa jurídica"}</dd>
          <dt className="text-muted-foreground">Documento</dt><dd className="break-all tabular-nums">{c.documentNumber || "Não informado"}</dd>
          <dt className="text-muted-foreground">Telefone</dt><dd>{c.phone || "Não informado"}</dd>
          {c.email && <><dt className="text-muted-foreground">E-mail</dt><dd className="break-all">{c.email}</dd></>}
          <dt className="text-muted-foreground">Cidade</dt><dd>{addr.city ? addr.city + (addr.state ? " / " + addr.state : "") : "Não informada"}</dd>
        </dl>
        <Link to={"/clientes/" + c.id} className="mt-3 inline-flex min-h-11 items-center text-sm font-medium text-primary" aria-label={"Editar " + c.name}>Editar cadastro →</Link>
      </li>;
    })}</ul>
  </>;
}
