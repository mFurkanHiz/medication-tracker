import type { Metadata } from "next";
import "./globals.css";
import { messages } from "@/lib/messages";

export const metadata: Metadata = {
  title: messages.tr.title,
  description: messages.tr.description,
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html
      lang="tr"
      className="h-full antialiased"
    >
      <body className="min-h-full flex flex-col">{children}</body>
    </html>
  );
}
