interface ShapeClasses {
  item: string;
  bg: string;
  focusRing: string;
  mergedBg: string;
  container: string;
  button: string;
  input: string;
}

const pillShape: ShapeClasses = {
  item: "rounded-[20px]",
  bg: "rounded-[20px]",
  focusRing: "rounded-[20px]",
  mergedBg: "rounded-2xl",
  container: "rounded-3xl",
  button: "rounded-[20px]",
  input: "rounded-[20px]",
};

export function useShape(): ShapeClasses {
  return pillShape;
}
