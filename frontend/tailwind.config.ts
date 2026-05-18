import type { Config } from 'tailwindcss';
import daisyui from 'daisyui';

const config: Config = {
  content: [
    './app/**/*.{ts,tsx}',
    './components/**/*.{ts,tsx}',
    './lib/**/*.{ts,tsx}',
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ['var(--font-sarabun)', 'var(--font-inter)', 'system-ui', 'sans-serif'],
        display: ['var(--font-inter)', 'var(--font-sarabun)', 'sans-serif'],
        mono: ['var(--font-jetbrains)', 'Consolas', 'monospace'],
      },
      animation: {
        'fade-in': 'fade-in 0.2s ease-out',
        'slide-up': 'slide-up 0.3s ease-out',
        'shimmer': 'shimmer 1.6s linear infinite',
      },
      keyframes: {
        'fade-in': {
          '0%': { opacity: '0' },
          '100%': { opacity: '1' },
        },
        'slide-up': {
          '0%': { opacity: '0', transform: 'translateY(8px)' },
          '100%': { opacity: '1', transform: 'translateY(0)' },
        },
        'shimmer': {
          '0%': { backgroundPosition: '-1000px 0' },
          '100%': { backgroundPosition: '1000px 0' },
        },
      },
    },
  },
  plugins: [
    require('@tailwindcss/forms'),
    require('@tailwindcss/typography'),
    daisyui,
  ],
  daisyui: {
    themes: [
      {
        teas: {
          // ---- Light theme — Professional Thai accounting ----
          'primary':           '#1565C0',   // Trustworthy blue
          'primary-content':   '#FFFFFF',
          'secondary':         '#0F766E',   // Teal accent
          'secondary-content': '#FFFFFF',
          'accent':            '#F59E0B',   // Amber highlight
          'accent-content':    '#1F2937',
          'neutral':           '#1F2937',
          'neutral-content':   '#F9FAFB',
          'base-100':          '#FFFFFF',   // Card / surface
          'base-200':          '#F8FAFC',   // Page bg
          'base-300':          '#E2E8F0',   // Border
          'base-content':      '#0F172A',
          'info':              '#0EA5E9',
          'success':           '#16A34A',
          'warning':           '#F59E0B',
          'error':             '#DC2626',
          '--rounded-box':     '0.5rem',
          '--rounded-btn':     '0.375rem',
          '--rounded-badge':   '0.375rem',
          '--animation-btn':   '0.2s',
          '--btn-text-case':   'none',
          '--border-btn':      '1px',
        },
      },
      {
        'teas-dark': {
          'primary':           '#60A5FA',
          'primary-content':   '#0B1220',
          'secondary':         '#2DD4BF',
          'secondary-content': '#0B1220',
          'accent':            '#FBBF24',
          'accent-content':    '#0B1220',
          'neutral':           '#1F2937',
          'neutral-content':   '#F9FAFB',
          'base-100':          '#0B1220',
          'base-200':          '#111827',
          'base-300':          '#1F2937',
          'base-content':      '#E5E7EB',
          'info':              '#38BDF8',
          'success':           '#22C55E',
          'warning':           '#FBBF24',
          'error':             '#F87171',
          '--rounded-box':     '0.5rem',
          '--rounded-btn':     '0.375rem',
          '--rounded-badge':   '0.375rem',
          '--btn-text-case':   'none',
        },
      },
    ],
    darkTheme: 'teas-dark',
    base: true,
    styled: true,
    utils: true,
    prefix: '',
    logs: false,
    themeRoot: ':root',
  },
};

export default config;
