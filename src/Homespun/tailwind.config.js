/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    './**/*.razor',
    './**/*.razor.cs',
    './wwwroot/**/*.html'
  ],
  theme: {
    extend: {
      colors: {
        // Primary color palette (matching app.css)
        'dark-background': '#202020',
        'basalt': '#3B3B3B',
        'basalt-light': '#767676',
        'red': '#DA1A32',
        'outback': '#580B07',
        'sand': '#FFF2DF',
        'wattle': '#FFEA76',
        'lagoon': '#36A390',
        'ocean': '#51A5C1',
        'gum': '#C2FFEF',

        // Semantic colors using CSS variables for theme support
        'bg-primary': 'var(--bg-primary)',
        'bg-secondary': 'var(--bg-secondary)',
        'bg-tertiary': 'var(--bg-tertiary)',
        'bg-hover': 'var(--bg-hover)',
        'bg-selected': 'var(--bg-selected)',
        'bg-card': 'var(--bg-card)',
        'bg-sidebar': 'var(--bg-sidebar)',
        'bg-sidebar-header': 'var(--bg-sidebar-header)',

        'text-primary': 'var(--text-primary)',
        'text-secondary': 'var(--text-secondary)',
        'text-muted': 'var(--text-muted)',
        'text-inverse': 'var(--text-inverse)',
        'text-sidebar': 'var(--text-sidebar)',
        'text-sidebar-muted': 'var(--text-sidebar-muted)',

        'border': 'var(--border-color)',
        'border-light': 'var(--border-color-light)',
        'border-selected': 'var(--border-selected)',

        'link': 'var(--link-color)',
        'link-hover': 'var(--link-hover)',

        // Button colors
        'btn-primary-bg': 'var(--btn-primary-bg)',
        'btn-primary-text': 'var(--btn-primary-text)',
        'btn-primary-hover': 'var(--btn-primary-hover)',
        'btn-secondary-bg': 'var(--btn-secondary-bg)',
        'btn-secondary-text': 'var(--btn-secondary-text)',
        'btn-secondary-hover': 'var(--btn-secondary-hover)',
        'btn-danger-bg': 'var(--btn-danger-bg)',
        'btn-danger-text': 'var(--btn-danger-text)',
        'btn-danger-hover': 'var(--btn-danger-hover)',

        // Status colors
        'status-success': 'var(--status-success)',
        'status-warning': 'var(--status-warning)',
        'status-error': 'var(--status-error)',
        'status-info': 'var(--status-info)',
        'status-muted': 'var(--status-muted)',
        'status-merged': 'var(--status-merged)',
        'status-conflict': 'var(--status-conflict)',

        // Section colors
        'section-past-bg': 'var(--section-past-bg)',
        'section-current-bg': 'var(--section-current-bg)',
        'section-future-bg': 'var(--section-future-bg)',

        // Form colors
        'input-bg': 'var(--input-bg)',
        'input-border': 'var(--input-border)',
        'input-focus-border': 'var(--input-focus-border)',
        'input-placeholder': 'var(--input-placeholder)',

        // Misc
        'code-bg': 'var(--code-bg)',
      },
      fontFamily: {
        'base': ['Figtree', '-apple-system', 'BlinkMacSystemFont', 'Segoe UI', 'Roboto', 'sans-serif'],
      },
      fontWeight: {
        'normal': '400',
        'medium': '500',
        'semibold': '600',
      },
      spacing: {
        'xs': '0.125rem',  // 2px
        'sm': '0.25rem',   // 4px
        'md': '0.5rem',    // 8px
        'lg': '0.75rem',   // 12px
        'xl': '1rem',      // 16px
      },
      borderRadius: {
        'sm': '0.25rem',
        'md': '0.375rem',
        'lg': '0.5rem',
        'full': '9999px',
      },
      transitionDuration: {
        'fast': '150ms',
        'normal': '250ms',
      },
      boxShadow: {
        'sm': 'var(--shadow-sm)',
        'md': 'var(--shadow-md)',
        'lg': 'var(--shadow-lg)',
      },
      fontSize: {
        'xs': ['0.75rem', { lineHeight: '1rem' }],       // 12px
        'sm': ['0.8125rem', { lineHeight: '1.25rem' }],  // 13px
        'base': ['0.875rem', { lineHeight: '1.5rem' }],  // 14px (base)
        'lg': ['1rem', { lineHeight: '1.75rem' }],       // 16px
        'xl': ['1.125rem', { lineHeight: '1.75rem' }],   // 18px
        '2xl': ['1.25rem', { lineHeight: '1.75rem' }],   // 20px
        '3xl': ['1.5rem', { lineHeight: '2rem' }],       // 24px
      },
    },
  },
  plugins: [],
}
