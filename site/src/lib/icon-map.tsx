import type { ComponentType } from "react";
import { ChevronRight, Copy } from "lucide-react";

interface IconComponentProps {
  size?: number;
  strokeWidth?: number;
  className?: string;
}

export type IconComponent = ComponentType<IconComponentProps>;
export type IconName = "chevron-right" | "copy";

export const iconMap: Record<IconName, IconComponent> = {
  "chevron-right": ChevronRight,
  "copy": Copy,
};
