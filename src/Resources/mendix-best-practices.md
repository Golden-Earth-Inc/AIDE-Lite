# Mendix Best Practices (Condensed)

## XPath Syntax Reference (CRITICAL — follow exactly)
The xpathConstraint parameter is the constraint only (with brackets). The entity is a separate parameter.

### Basic Syntax
- String: [Name = 'John'] — single quotes for string values
- Number: [Amount > 100], [Count >= 5], [Price != 0]
- Boolean: [IsActive = true()] and [IsActive = false()] — true() and false() are FUNCTIONS with parentheses
- Boolean shorthand: [IsActive] means true, [not(IsActive)] means false
- Empty check: [Name != empty], [Description = empty] — use "empty" keyword, NOT "null"
- Enum: [Status = 'Active'] — use the enum VALUE NAME as a string

### Association Traversal (MUST be module-qualified)
To filter by an attribute on an associated entity, use the FULL path:
  [ModuleName.AssociationName/ModuleName.TargetEntity/Attribute = 'value']

Example — Given: Entity Order in module Sales, association Order_Customer (*→1 Customer):
  CORRECT: [Sales.Order_Customer/Sales.Customer/Name = 'Acme']
  WRONG:   [Order_Customer/Customer/Name = 'Acme'] — missing module prefix
  WRONG:   [Customer/Name = 'Acme'] — missing association path
  WRONG:   [Sales.Customer/Name = 'Acme'] — missing association name

Simple attributes do NOT need module prefix: [Status = 'Open'] — correct
Associations and entities in paths ALWAYS need module prefix: [Module.Assoc/Module.Entity/Attr]

### Nested Associations (max 2 deep)
[Module.Assoc1/Module.Entity1/Module.Assoc2/Module.Entity2/Attribute = 'value']

### Logical Operators
- AND: [IsActive = true() and Status = 'Open'] — lowercase "and"
- OR: [Status = 'Open' or Status = 'InProgress'] — lowercase "or"
- NOT: [not(Status = 'Closed')]
- Grouping: [(Status = 'Open' or Status = 'InProgress') and IsActive = true()]

### Date/Time Tokens (wrap in single quotes)
- Current date/time: [CreatedDate > '[%CurrentDateTime%]']
- Begin of today: [OrderDate >= '[%BeginOfCurrentDay%]']
- End of today: [OrderDate < '[%EndOfCurrentDay%]']
- Days from now: [DueDate < '[%CurrentDateTime%] + 7']
- Begin of current week/month/year: [%BeginOfCurrentWeek%], [%BeginOfCurrentMonth%], [%BeginOfCurrentYear%]

### Current User Token
To filter by current user, navigate via the System.Account association:
  [ModuleName.Entity_Account/System.Account/id = '[%CurrentUser%]']

### String Functions
- contains(AttributeName, 'text') — substring search (case-sensitive)
- starts-with(AttributeName, 'prefix')
- string-length(AttributeName) > 0

### Common XPath Examples
Given module "MyModule" with Order (Status:Enum, TotalAmount:Decimal, IsUrgent:Boolean),
association Order_Customer (*→1 Customer with Name:String):

| Goal | XPath Constraint |
|------|-----------------|
| All open orders | [Status = 'Open'] |
| Orders over $100 | [TotalAmount > 100] |
| Urgent open orders | [IsUrgent = true() and Status = 'Open'] |
| Orders for customer 'Acme' | [MyModule.Order_Customer/MyModule.Customer/Name = 'Acme'] |
| Orders created today | [CreatedDate >= '[%BeginOfCurrentDay%]'] |
| Orders without a customer | [MyModule.Order_Customer = empty] |
| Non-cancelled orders | [not(Status = 'Cancelled')] |

### XPath Performance
- Add indexes on attributes used in XPath constraints and sorts
- Avoid not(), contains() on large datasets — use starts-with() or Boolean flags
- Max 2 associations deep in XPath; break into multiple retrieves beyond that
- Retrieve All + List Filter = anti-pattern when XPath constraint would work

## OQL Syntax Reference (CRITICAL — follow exactly)

### Association JOIN Syntax (the #1 OQL rule)
- NEVER use SQL-style ON conditions: `LEFT JOIN Entity AS e ON e/Association = source`
- ALWAYS use Mendix association path syntax: `source/Module.Association_Name/Module.Entity AS e`
- Association paths MUST start from an entity already in query context (FROM or previous JOIN)
- Format: `source_alias/Module.AssociationName/Module.TargetEntity AS target_alias`
- Use LEFT OUTER JOIN for optional relationships
- Example:
  ```
  FROM GroupView.DayGroup AS dg
  INNER JOIN dg/GroupView.DayGroup_Timesheet/Timesheets.Timesheet AS ts
  LEFT OUTER JOIN dg/GroupView.DayGroupAssignment_DayGroup/GroupView.DayGroupAssignment AS dga
  ```

### Reserved Words Handling
- Source columns that are reserved words MUST be double-quoted: `entity."From"`, `entity."Date"`, `entity."Day"`
- Column aliases MUST NEVER be reserved words — use descriptive names instead
- Common reserved words: From, To, Date, Day, Group, Status, Order, User, Time, Year, Month
- Correct: `dg."Day" AS DayOfWeek`, `ts."From" AS TimesheetFrom`
- Wrong: `dg."Day" AS "Day"` — alias is reserved word

### Association Path Verification (before writing any JOIN)
- Verify the association owner from the entity details in the APP MODEL
- Check the exact association name (case-sensitive)
- Confirm the multiplicity (OneToMany, ManyToOne, OneToOne, ManyToMany)
- Determine the correct traversal direction
- Path pattern: `currentAlias/OwnerModule.AssociationName/TargetModule.TargetEntity`

### Entity and Module Qualification
- Always use full module paths: `Module.Entity` format
- Include module prefix for both associations and entities
- Verify exact module names from the APP MODEL

### Aliasing Conventions
- Use consistent, meaningful short aliases (e.g., `dg` for DayGroup, `ts` for Timesheet)
- Aliases must not conflict with reserved words
- All fields MUST have aliases in OQL

### SELECT Syntax
- `SELECT alias.Attribute AS AliasName FROM Module.Entity AS alias`
- DISTINCT: `SELECT DISTINCT ...`
- Format multi-column SELECT lists with one column per line

### WHERE Clause
- String: `WHERE o.Name = 'John'` (single quotes)
- Number: `WHERE o.Amount > 100`
- Boolean: `WHERE o.IsActive = true` (no parentheses — unlike XPath)
- NULL: `WHERE o.Description IS NULL` / `IS NOT NULL`
- Enum: `WHERE o.Status = 'Active'` (enum value name as string)
- AND/OR: `WHERE o.Status = 'Open' AND o.IsUrgent = true`
- NOT: `WHERE NOT (o.Status = 'Closed')`
- IN: `WHERE o.Status IN ('Open', 'InProgress')`
- LIKE: `WHERE o.Name LIKE '%search%'`

### Aggregates, GROUP BY, HAVING
- `SELECT o.Status, COUNT(*), SUM(o.Amount) FROM Module.Entity AS o GROUP BY o.Status`
- `HAVING COUNT(*) > 5`

### ORDER BY, LIMIT/OFFSET
- ORDER BY REQUIRES a LIMIT or OFFSET — OQL will error without one. View entities have no inherent order; sorting only defines data subsets with LIMIT/OFFSET. Sort at the presentation layer (e.g., Retrieve microflow activity) when you need display ordering without pagination.
- `ORDER BY o.CreatedDate DESC LIMIT 100 OFFSET 0`
- If you only need sorting without pagination, do NOT use ORDER BY in OQL — sort in the Retrieve activity or list operation instead
- NEVER add ORDER BY, LIMIT, or OFFSET unless the user explicitly requests sorting, pagination, or a limited result set. By default, return all matching rows unsorted.

### Date Functions
- `DATEPART(YEAR, o.CreatedDate)`
- `DATEDIFF(DAY, o.StartDate, o.EndDate)`

### Query Structure Requirements
- Use uppercase for SQL keywords: SELECT, FROM, INNER JOIN, LEFT OUTER JOIN, WHERE, ORDER BY
- Maintain clear visual separation between query sections
- Proper indentation for readability
- Never add comment lines in OQL queries

### Key Differences from SQL
| SQL | Mendix OQL |
|-----|------------|
| `table_name` | `Module.EntityName` (always qualified) |
| `JOIN ... ON a.id = b.fk` | `JOIN alias/Module.Association/Module.Entity` |
| `true`/`false` | `true`/`false` (no parentheses) |
| `NULL` | `NULL` (same, use IS NULL) |

### OQL Verification Checklist
Before finalizing any OQL query, verify:
- All reserved words in source columns are quoted
- No reserved words used as column aliases
- All JOINs use association path syntax (no ON clauses)
- All association paths start from entities already in query context
- Association names and directions verified against APP MODEL
- Module prefixes included for all entities and associations
- All fields have aliases
- Query is properly indented and formatted
- No comment lines in the query

### OQL Error Prevention
If any entity, association name, or path is ambiguous:
- STOP and check the APP MODEL first
- Verify the exact entity name, module, and association details
- Confirm association ownership and multiplicity
- Only then construct the JOIN path
- Never assume association names or directions — always verify

## Performance
- NEVER do database calls inside loops (N+1 problem). Retrieve list before loop, filter in memory
- Use Aggregate List (COUNT/SUM/AVG/MIN/MAX) over retrieve-and-count — runs at DB level
- Batch commits: commit once after loop, not inside. Mendix auto-commits at end of client action
- Use Retrieve by Association when object already in memory, not database Retrieve with XPath
- Range-limit retrieves for paginated views. Use First for existence checks

## Domain Model
- Decimal for money (not Float — precision issues). Enum for fixed values (not String)
- Avoid 50+ attributes per entity — split into one-to-one child entity
- Generalization for true is-a only. 3+ levels = wide tables with nulls
- Always specify delete behavior on associations explicitly
- Prefer *-to-1 over *-to-* associations unless genuinely many-to-many

## Memory
- Large lists stay in memory until microflow ends — use Range constraints
- Batch-commit pattern for 1000+ objects: retrieve in batches of 500, process, commit
- Non-persistable entities (NPEs) consume server memory per session — paginate large result sets

## Security
- Entity access rules = ONLY reliable security boundary (not page visibility)
- XPath constraints in entity access for row-level security
- Validate inputs in all exposed microflows — don't trust client-side validation

## Error Handling
- Error handlers on REST calls, constrained DB ops, file ops
- Log $latestError/Message and $latestError/StackTrace in error handlers
- VAL_ microflows for validation before commit

## Naming
- ACT_ (action), SUB_ (sub-microflow), DS_ (data source), VAL_ (validation)
- SE_ (scheduled event), BCO_/ACO_ (before/after commit), BDE_/ADE_ (before/after delete)
- Entities: singular PascalCase. Booleans: Is/Has/Can/Should prefix
- Separate modules by functional domain, not technical layer

## Common Mistakes
- Commit inside loop → commit once after
- Retrieve inside loop → retrieve before, filter in memory
- Unconstrained retrieve on large entity → add XPath/Range
- Float for money → Decimal
- Modifying Marketplace modules → extend in wrapper module
- No error handling on REST calls → add handler + timeout
- Calculated attributes in XPath → cannot be indexed, store explicitly

## Data Flow Rules (CRITICAL — follow strictly)
- A variable MUST be created before it can be used: Retrieve → then AggregateList/Sort/Filter on that list
- AggregateList requires an existing list variable. NEVER put AggregateList before the Retrieve that creates the list
- AssociationRetrieve requires the starting object to exist. Retrieve or receive it as parameter first
- ChangeObject requires the object variable to exist. Create or Retrieve it first
- Commit/Delete/Rollback require an object variable. Never commit a variable that hasn't been created
- Sort, FilterByAttribute, FilterByAssociation, FindByExpression all require an existing list variable
- ListOperation (Union, Intersect, Subtract) requires BOTH list variables to exist
- MicroflowCall output variables are available to all subsequent activities
- Parameter variables are available from the start of the microflow

## Microflow Patterns
- CRUD: Retrieve → ChangeObject → Commit (read-modify-persist)
- Validation: Retrieve → check attributes → return Boolean
- List processing: Retrieve list → (loop body in SUB_) → commit results
- Count check: Retrieve list → AggregateList(Count) → use $Count in decision
- Association navigation: Retrieve parent → AssociationRetrieve children → process list
- Search: Retrieve with XPath constraint → return list or first object
- Create with defaults: CreateObject with memberChanges → Commit

## Anti-Patterns (NEVER do these)
- WRONG: AggregateList → Retrieve (counting before fetching)
- WRONG: Commit → CreateObject (committing before creating)
- WRONG: ChangeObject → Retrieve (changing before getting the object)
- WRONG: Sort → Retrieve (sorting a list that doesn't exist)
- WRONG: Retrieve inside loop body → Retrieve before loop, filter in memory
- WRONG: Commit inside loop body → Commit once after loop
- WRONG: Retrieve All then filter in microflow → Use XPath constraint
- WRONG: Float for currency → Use Decimal
