import { useQuery } from "@tanstack/react-query";
import { getPublicMenu } from "../api/portal.api";

export function usePublicMenu(slug: string) {
  return useQuery({
    queryKey: ["public-menu", slug],
    queryFn:  () => getPublicMenu(slug),
    staleTime: 5 * 60 * 1000,
    retry: false,
  });
}
