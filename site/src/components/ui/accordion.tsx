"use client";

import {
  useRef,
  useState,
  useEffect,
  useCallback,
  createContext,
  useContext,
  forwardRef,
  type ComponentPropsWithoutRef,
  type ReactNode,
  type HTMLAttributes,
} from "react";
import { motion, AnimatePresence } from "framer-motion";
import * as AccordionPrimitive from "@radix-ui/react-accordion";
import { cn } from "@/lib/utils";
import { useIcon } from "@/lib/icon-context";
import { springs } from "@/lib/springs";
import { fontWeights } from "@/lib/font-weight";
import { useProximityHover } from "@/hooks/use-proximity-hover";
import { useShape } from "@/lib/shape-context";

// ─── Contexts ────────────────────────────────────────────────────────────────

interface ItemRect {
  top: number;
  left: number;
  width: number;
  height: number;
}

interface AccordionGroupContextValue {
  registerItem: (index: number, element: HTMLElement | null) => void;
  registerFullItem: (index: number, element: HTMLElement | null) => void;
  activeIndex: number | null;
  remeasure: () => void;
  openValues: Set<string>;
}

const AccordionGroupContext =
  createContext<AccordionGroupContextValue | null>(null);

function useAccordionGroup() {
  const ctx = useContext(AccordionGroupContext);
  if (!ctx)
    throw new Error(
      "AccordionItem/AccordionTrigger/AccordionContent must be used within an AccordionGroup"
    );
  return ctx;
}

interface AccordionItemContextValue {
  index?: number;
  isOpen: boolean;
}

const AccordionItemContext =
  createContext<AccordionItemContextValue | null>(null);

function useAccordionItemContext() {
  const ctx = useContext(AccordionItemContext);
  if (!ctx)
    throw new Error(
      "AccordionTrigger/AccordionContent must be used within an AccordionItem"
    );
  return ctx;
}

// ─── AccordionGroup ──────────────────────────────────────────────────────────

type AccordionGroupSingleProps = {
  type?: "single";
  value?: string;
  defaultValue?: string;
  onValueChange?: (value: string) => void;
  collapsible?: boolean;
};

type AccordionGroupMultipleProps = {
  type: "multiple";
  value?: string[];
  defaultValue?: string[];
  onValueChange?: (value: string[]) => void;
};

type AccordionGroupProps = HTMLAttributes<HTMLDivElement> & {
  children: ReactNode;
} & (AccordionGroupSingleProps | AccordionGroupMultipleProps);

function getAccordionHtmlProps(
  props: AccordionGroupProps
): HTMLAttributes<HTMLDivElement> {
  if (props.type === "multiple") {
    const {
      children: _children,
      className: _className,
      type: _type,
      value: _value,
      defaultValue: _defaultValue,
      onValueChange: _onValueChange,
      ...htmlProps
    } = props;
    return htmlProps;
  }

  const {
    children: _children,
    className: _className,
    type: _type,
    value: _value,
    defaultValue: _defaultValue,
    onValueChange: _onValueChange,
    collapsible: _collapsible,
    ...htmlProps
  } = props;
  return htmlProps;
}

const AccordionGroup = forwardRef<HTMLDivElement, AccordionGroupProps>(
  (props, ref) => {
    const { children, className } = props;
    const multipleProps = props.type === "multiple" ? props : null;
    const singleProps = props.type === "multiple" ? null : props;
    const type = multipleProps ? "multiple" : "single";

    const containerRef = useRef<HTMLDivElement>(null);
    const fullItemElementsRef = useRef<Map<number, HTMLElement>>(new Map());
    const [openItemRects, setOpenItemRects] = useState<Map<number, ItemRect>>(
      new Map()
    );

    const {
      activeIndex,
      setActiveIndex,
      itemRects,
      sessionRef,
      handlers,
      registerItem,
      measureItems,
    } = useProximityHover(containerRef);

    const registerFullItem = useCallback(
      (index: number, element: HTMLElement | null) => {
        if (element) {
          fullItemElementsRef.current.set(index, element);
        } else {
          fullItemElementsRef.current.delete(index);
        }
      },
      []
    );

    const measureFullItems = useCallback(() => {
      if (!containerRef.current) return;
      const containerRect = containerRef.current.getBoundingClientRect();
      const next = new Map<number, ItemRect>();
      fullItemElementsRef.current.forEach((el, idx) => {
        const r = el.getBoundingClientRect();
        next.set(idx, {
          top: r.top - containerRect.top,
          left: r.left - containerRect.left,
          width: r.width,
          height: r.height,
        });
      });
      setOpenItemRects(next);
    }, []);

    // Track open values for context
    const [internalSingleValue, setInternalSingleValue] = useState<string>(
      () => singleProps?.defaultValue ?? ""
    );
    const [internalMultipleValue, setInternalMultipleValue] = useState<
      string[]
    >(() => multipleProps?.defaultValue ?? []);
    const singleOnValueChange = singleProps?.onValueChange;
    const multipleOnValueChange = multipleProps?.onValueChange;
    const singleValue = singleProps?.value ?? internalSingleValue;

    const openValues = new Set<string>(
      multipleProps
        ? multipleProps.value ?? internalMultipleValue
        : singleValue
          ? [singleValue]
          : []
    );

    const handleSingleValueChange = useCallback(
      (value: string) => {
        if (singleOnValueChange) singleOnValueChange(value);
        else setInternalSingleValue(value);
      },
      [singleOnValueChange]
    );

    const handleMultipleValueChange = useCallback(
      (value: string[]) => {
        if (multipleOnValueChange) multipleOnValueChange(value);
        else setInternalMultipleValue(value);
      },
      [multipleOnValueChange]
    );

    useEffect(() => {
      measureItems();
      measureFullItems();
    }, [measureItems, measureFullItems, children]);

    // Remeasure synchronously when open values change so the first
    // paint already reflects shifted trigger positions.
    const openValuesKey = [...openValues].join(",");

    useEffect(() => {
      measureItems();
      measureFullItems();
    }, [measureItems, measureFullItems, openValuesKey]);

    const [focusedIndex, setFocusedIndex] = useState<number | null>(null);

    const activeRect = activeIndex !== null ? itemRects[activeIndex] : null;
    const focusRect = focusedIndex !== null ? itemRects[focusedIndex] : null;
    // Dimming: reduce expanded BG opacity when hovering a non-expanded trigger
    const isHoveringNonOpen =
      activeIndex !== null && !openItemRects.has(activeIndex);
    const shape = useShape();

    const htmlProps = getAccordionHtmlProps(props);

    // Build Radix root props
    const radixProps =
      type === "multiple"
        ? {
            type: "multiple" as const,
            value: multipleProps?.value ?? internalMultipleValue,
            onValueChange: handleMultipleValueChange,
          }
        : {
            type: "single" as const,
            collapsible: singleProps?.collapsible ?? true,
            value: singleProps?.value ?? internalSingleValue,
            onValueChange: handleSingleValueChange,
          };

    return (
      <AccordionGroupContext.Provider
        value={{
          registerItem,
          registerFullItem,
          activeIndex,
          remeasure: () => {
            measureItems();
            measureFullItems();
          },
          openValues,
        }}
      >
        <AccordionPrimitive.Root {...radixProps} asChild>
          <div
            ref={(node) => {
              containerRef.current = node;
              if (typeof ref === "function") ref(node);
              else if (ref) ref.current = node;
            }}
            onMouseEnter={handlers.onMouseEnter}
            onMouseMove={handlers.onMouseMove}
            onMouseLeave={handlers.onMouseLeave}
            onFocus={(e) => {
              if (!(e.target instanceof HTMLElement)) return;
              const indexAttr = e.target
                .closest("[data-proximity-index]")
                ?.getAttribute("data-proximity-index");
              if (indexAttr != null) {
                const idx = Number(indexAttr);
                setActiveIndex(idx);
                setFocusedIndex(
                  e.target.matches(":focus-visible")
                    ? idx
                    : null
                );
              }
            }}
            onBlur={(e) => {
              if (
                e.relatedTarget instanceof Node &&
                containerRef.current?.contains(e.relatedTarget)
              )
                return;
              setFocusedIndex(null);
              setActiveIndex(null);
            }}
            className={cn(
              "relative flex flex-col gap-0.5 w-72 max-w-full select-none",
              className
            )}
            {...htmlProps}
          >
            {/* Expanded item backgrounds */}
            <AnimatePresence>
              {[...openItemRects.entries()].map(([idx, rect]) => (
                <motion.div
                  key={`expanded-${idx}`}
                  className={`absolute ${shape.bg} bg-accent/20 dark:bg-accent/12 pointer-events-none`}
                  initial={false}
                  animate={{
                    top: rect.top,
                    left: rect.left,
                    width: rect.width,
                    height: rect.height,
                    opacity: isHoveringNonOpen ? 0.7 : 1,
                  }}
                  exit={{ opacity: 0, transition: { duration: 0.12 } }}
                  transition={{
                    ...springs.moderate,
                    opacity: { duration: 0.08 },
                  }}
                />
              ))}
            </AnimatePresence>

            {/* Hover background */}
            <AnimatePresence>
              {activeRect && (
                <motion.div
                  key={sessionRef.current}
                  className={`absolute ${shape.bg} bg-accent/40 dark:bg-accent/25 pointer-events-none`}
                  initial={{
                    opacity: 0,
                    top: activeRect.top,
                    left: activeRect.left,
                    width: activeRect.width,
                    height: activeRect.height,
                  }}
                  animate={{
                    opacity: 1,
                    top: activeRect.top,
                    left: activeRect.left,
                    width: activeRect.width,
                    height: activeRect.height,
                  }}
                  exit={{ opacity: 0, transition: { duration: 0.06 } }}
                  transition={{
                    ...springs.fast,
                    opacity: { duration: 0.08 },
                  }}
                />
              )}
            </AnimatePresence>

            {/* Focus ring */}
            <AnimatePresence>
              {focusRect && (
                <motion.div
                  className={`absolute ${shape.focusRing} pointer-events-none z-20 border border-[#6B97FF]`}
                  initial={false}
                  animate={{
                    left: focusRect.left - 2,
                    top: focusRect.top - 2,
                    width: focusRect.width + 4,
                    height: focusRect.height + 4,
                  }}
                  exit={{ opacity: 0, transition: { duration: 0.06 } }}
                  transition={{
                    ...springs.fast,
                    opacity: { duration: 0.08 },
                  }}
                />
              )}
            </AnimatePresence>

            {children}
          </div>
        </AccordionPrimitive.Root>
      </AccordionGroupContext.Provider>
    );
  }
);

AccordionGroup.displayName = "AccordionGroup";

// ─── AccordionItem ───────────────────────────────────────────────────────────

type AccordionItemProps = ComponentPropsWithoutRef<
  typeof AccordionPrimitive.Item
> & {
  index?: number;
};

const AccordionItem = forwardRef<HTMLDivElement, AccordionItemProps>(
  ({ value, index, disabled, children, className, ...props }, ref) => {
    const internalRef = useRef<HTMLDivElement>(null);
    const groupCtx = useAccordionGroup();
    const isOpen = groupCtx.openValues.has(value);

    // Register full item element for proximity hover (covers trigger + content)
    useEffect(() => {
      if (index !== undefined) {
        groupCtx.registerItem(index, internalRef.current);
        return () => groupCtx.registerItem(index, null);
      }
    }, [index, groupCtx]);

    // Register full item element for expanded background measurement
    useEffect(() => {
      if (index !== undefined) {
        if (isOpen) {
          groupCtx.registerFullItem(index, internalRef.current);
        } else {
          groupCtx.registerFullItem(index, null);
        }
        return () => groupCtx.registerFullItem(index, null);
      }
    }, [index, groupCtx, isOpen]);

    return (
      <AccordionItemContext.Provider value={{ index, isOpen }}>
        <AccordionPrimitive.Item
          ref={(node) => {
            internalRef.current = node;
            if (typeof ref === "function") ref(node);
            else if (ref) ref.current = node;
          }}
          value={value}
          disabled={disabled}
          data-proximity-index={index}
          className={cn("relative", className)}
          {...props}
        >
          {children}
        </AccordionPrimitive.Item>
      </AccordionItemContext.Provider>
    );
  }
);

AccordionItem.displayName = "AccordionItem";

// ─── AccordionTrigger ────────────────────────────────────────────────────────

type AccordionTriggerProps = ComponentPropsWithoutRef<
  typeof AccordionPrimitive.Trigger
>;

const AccordionTrigger = forwardRef<HTMLButtonElement, AccordionTriggerProps>(
  ({ children, className, ...props }, ref) => {
    const ChevronRight = useIcon("chevron-right");
    const groupCtx = useAccordionGroup();
    const { index, isOpen } = useAccordionItemContext();
    const shape = useShape();
    const isActive = groupCtx.activeIndex === index;

    const triggerContent = (
      <AccordionPrimitive.Header asChild>
        <div>
          <AccordionPrimitive.Trigger
            ref={ref}
            className={cn(
              `relative z-10 flex items-center gap-2.5 ${shape.item} px-3 py-2 w-full cursor-pointer outline-none`,
              className
            )}
            {...props}
          >
            {/* Label with dual-layer text */}
            <span className="inline-grid text-[13px] flex-1 text-left">
              <span
                className="col-start-1 row-start-1 invisible"
                style={{ fontVariationSettings: fontWeights.semibold }}
                aria-hidden="true"
              >
                {children}
              </span>
              <span
                className={cn(
                  "col-start-1 row-start-1 transition-[color,font-variation-settings] duration-80",
                  isOpen || isActive
                    ? "text-foreground"
                    : "text-muted-foreground"
                )}
                style={{
                  fontVariationSettings:
                    isOpen ? fontWeights.semibold : fontWeights.normal,
                }}
              >
                {children}
              </span>
            </span>

            {/* Chevron — right when collapsed, rotates 90° down when expanded */}
            <motion.span
              className="shrink-0 inline-flex items-center justify-center"
              animate={{ rotate: isOpen ? 90 : 0 }}
              transition={springs.fast}
            >
              <ChevronRight
                size={16}
                strokeWidth={isOpen || isActive ? 2 : 1.5}
                className={cn(
                  "transition-[color,stroke-width] duration-80",
                  isOpen || isActive
                    ? "text-foreground"
                    : "text-muted-foreground"
                )}
              />
            </motion.span>
          </AccordionPrimitive.Trigger>
        </div>
      </AccordionPrimitive.Header>
    );

    return triggerContent;
  }
);

AccordionTrigger.displayName = "AccordionTrigger";

// ─── AccordionContent ────────────────────────────────────────────────────────

type AccordionContentProps = ComponentPropsWithoutRef<
  typeof AccordionPrimitive.Content
>;

const AccordionContent = forwardRef<HTMLDivElement, AccordionContentProps>(
  ({ children, className, ...props }, ref) => {
    const groupCtx = useAccordionGroup();
    const { isOpen } = useAccordionItemContext();

    return (
      <AnimatePresence initial={false}>
        {isOpen && (
          <AccordionPrimitive.Content forceMount asChild {...props}>
            <motion.div
              ref={ref}
              className={cn("overflow-hidden", className)}
              initial={{ height: 0 }}
              animate={{ height: "auto" }}
              exit={{ height: 0 }}
              transition={springs.moderate}
              onUpdate={() => {
                groupCtx.remeasure();
              }}
              onAnimationComplete={() => {
                groupCtx.remeasure();
              }}
            >
              <div className="px-3 pb-3 pt-1 text-[13px] text-muted-foreground">
                {children}
              </div>
            </motion.div>
          </AccordionPrimitive.Content>
        )}
      </AnimatePresence>
    );
  }
);

AccordionContent.displayName = "AccordionContent";

// ─── Exports ─────────────────────────────────────────────────────────────────

export {
  AccordionGroup,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
};
