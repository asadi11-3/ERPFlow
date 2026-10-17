// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');
const boundaries = require('eslint-plugin-boundaries');

module.exports = defineConfig([
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      '@angular-eslint/component-selector': [
        'error',
        {
          type: 'element',
          prefix: 'app',
          style: 'kebab-case',
        },
      ],
    },
  },
  {
    files: ['src/**/*.ts'],
    plugins: { boundaries },
    settings: {
      'boundaries/elements': [
        { type: 'core', pattern: 'src/app/core', partialMatch: false },
        { type: 'layout', pattern: 'src/app/layout', partialMatch: false },
        { type: 'ui', pattern: 'src/app/ui', partialMatch: false },
        { type: 'pattern', pattern: 'src/app/pattern', partialMatch: false },
        { type: 'feature', pattern: 'src/app/feature/*', capture: ['name'], partialMatch: false },
        { type: 'app', pattern: 'src/app', partialMatch: false },
      ],
      'boundaries/dependency-nodes': ['import', 'dynamic-import'],
      'import/resolver': { node: { extensions: ['.ts', '.mjs', '.js'] } },
    },
    rules: {
      'boundaries/dependencies': [
        'error',
        {
          default: 'disallow',
          policies: [
            {
              from: { element: { type: 'app' } },
              allow: { to: { element: { type: ['app', 'core', 'layout', 'ui', 'pattern'] } } },
            },
            {
              from: { element: { type: 'app' } },
              allow: { to: { element: { type: 'feature' } }, dependency: { nodeKind: 'dynamic-import' } },
            },
            { from: { element: { type: 'core' } }, allow: { to: { element: { type: 'core' } } } },
            {
              from: { element: { type: 'layout' } },
              allow: { to: { element: { type: ['layout', 'core', 'ui'] } } },
            },
            { from: { element: { type: 'ui' } }, allow: { to: { element: { type: 'ui' } } } },
            {
              from: { element: { type: 'pattern' } },
              allow: { to: { element: { type: ['pattern', 'core', 'ui'] } } },
            },
            {
              from: { element: { type: 'feature' } },
              allow: { to: { element: { type: ['core', 'ui', 'pattern'] } } },
            },
          ],
        },
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {},
  },
]);
