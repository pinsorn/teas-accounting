import type { Config } from 'tailwindcss';
import daisyui from 'daisyui';
import forms from '@tailwindcss/forms';
import typography from '@tailwindcss/typography';

const config: Config = {
  content: [
    './app/**/*.{ts,tsx}',
    './components/**/*.{ts,tsx}',
    './lib/**/*.{ts,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        // TEAS brand — warm peach + ink black (Sprint 13j-FE)
        peach: {
          50: '#FBF1E8',
          100: '#F8E3D0',
          200: '#F2CDB0',
          300: '#ECB68F',
          400: '#E8A87C', // logo orange
          500: '#DD8E5C', // primary CTA
          600: '#C57543', // primary hover
          700: '#9E5C34', // primary ink / accent text
        },
        ink: {
          50: '#FAF8F5',
          75: '#F4F1EC',
          100: '#ECE7DF',
          200: '#D7D1C7',
          300: '#B5AEA3',
          400: '#8A847A',
          500: '#6B6660',
          600: '#4D4943',
          700: '#34312D',
          800: '#24221F',
          900: '#1A1816', // logo black
        },
        // Warm-tinted status palette (token + soft bg)
        'status-success': '#4A7C59',
        'status-success-bg': '#E6EFE7',
        'status-warning': '#C68A2E',
        'status-warning-bg': '#FBEFD7',
        'status-danger': '#B5524A',
        'status-danger-bg': '#FBE4E1',
        'status-info': '#5B7B9A',
        'status-info-bg': '#E5ECF2',
        'status-draft': '#8A847A',
        'status-draft-bg': '#ECE7DF',
      },
      fontFamily: {
        sans: ['var(--font-noto-thai)', 'var(--font-inter)', 'system-ui', 'sans-serif'],
        ui: ['var(--font-noto-thai)', 'var(--font-inter)', 'system-ui', 'sans-serif'],
        doc: ['var(--font-sarabun)', '"TH Sarabun New"', 'serif'],
        display: ['var(--font-inter)', 'var(--font-sarabun)', 'sans-serif'],
        mono: ['var(--font-jetbrains)', 'Consolas', 'monospace'],
      },
      boxShadow: {
        'warm-sm': '0 1px 2px rgba(26,24,22,0.06)',
        'warm-md': '0 4px 12px rgba(26,24,22,0.08), 0 1px 3px rgba(26,24,22,0.04)',
        'warm-lg': '0 12px 32px rgba(26,24,22,0.12), 0 4px 8px rgba(26,24,22,0.06)',
        'warm-pop': '0 24px 60px rgba(26,24,22,0.18)',
      },
      borderRadius: {
        // Semantic warm-scale radii — named to avoid colliding with
        // Tailwind's rounded-{side}-{size} shorthands (rounded-r-lg etc.)
        chip: '6px',
        field: '10px',
        card: '14px',
        panel: '18px',
      },
      spacing: {
        sidebar: '256px',
        'sidebar-collapsed': '72px',
        topbar: '56px',
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
    forms,
    typography,
    daisyui,
  ],
  daisyui: {
    themes: [
      {
        'teas-orange': {
          // ---- Sprint 13j-FE — warm peach + ink (Claude Design) ----
          'primary':           '#DD8E5C',   // peach-500 CTA
          'primary-content':   '#FFFFFF',
          'secondary':         '#9E5C34',   // peach-700 accent ink
          'secondary-content': '#FFFFFF',
          'accent':            '#E8A87C',   // peach-400 logo orange
          'accent-content':    '#1A1816',
          'neutral':           '#1A1816',   // ink-900
          'neutral-content':   '#FAF8F5',
          'base-100':          '#FFFFFF',   // card / surface
          'base-200':          '#FAF8F5',   // page bg (ink-50)
          'base-300':          '#F4F1EC',   // surface-alt (ink-75)
          'base-content':      '#1A1816',   // ink-900 text
          'info':              '#5B7B9A',
          'success':           '#4A7C59',
          'warning':           '#C68A2E',
          'error':             '#B5524A',
          '--rounded-box':     '14px',
          '--rounded-btn':     '10px',
          '--rounded-badge':   '999px',
          '--animation-btn':   '0.12s',
          '--btn-text-case':   'none',
          '--border-btn':      '1px',
        },
      },
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
