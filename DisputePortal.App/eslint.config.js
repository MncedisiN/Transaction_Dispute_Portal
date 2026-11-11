import js from '@eslint/js'
import globals from 'globals'
import tsParser from '@typescript-eslint/parser'
import tsPlugin from '@typescript-eslint/eslint-plugin'
import vue from 'eslint-plugin-vue'

const typedRules = {
  // Prettier owns layout; ESLint retains Vue's correctness and component rules.
  'vue/html-indent': 'off',
  'vue/html-closing-bracket-newline': 'off',
  'vue/html-closing-bracket-spacing': 'off',
  'vue/first-attribute-linebreak': 'off',
  'vue/max-attributes-per-line': 'off',
  'vue/singleline-html-element-content-newline': 'off',
  'vue/multiline-html-element-content-newline': 'off',
  'vue/html-self-closing': 'off',
  'no-undef': 'off',
  'no-unused-vars': 'off',
  'vue/multi-word-component-names': 'off',
  '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }]
}

export default [
  {
    ignores: [
      'dist/**',
      'node_modules/**',
      'coverage/**',
      'playwright-report/**',
      'test-results/**'
    ]
  },
  js.configs.recommended,
  {
    files: ['*.config.js', '*.config.mjs', 'scripts/**/*.mjs', 'config/**/*.mjs'],
    languageOptions: { globals: globals.node }
  },
  { files: ['public/push-*.js'], languageOptions: { globals: globals.serviceworker } },
  ...vue.configs['flat/recommended'],
  // Unit tests intentionally mount independent fixture components in the same spec.
  { files: ['src/**/*.test.ts'], rules: { 'vue/one-component-per-file': 'off' } },
  {
    files: ['**/*.ts'],
    languageOptions: {
      parser: tsParser,
      parserOptions: { ecmaVersion: 'latest', sourceType: 'module' },
      globals: { ...globals.browser, ...globals.node }
    },
    plugins: { '@typescript-eslint': tsPlugin },
    rules: typedRules
  },
  {
    files: ['**/*.vue'],
    languageOptions: {
      parserOptions: { parser: tsParser, ecmaVersion: 'latest', sourceType: 'module' },
      globals: { ...globals.browser, ...globals.node }
    },
    plugins: { '@typescript-eslint': tsPlugin },
    rules: typedRules
  }
]
