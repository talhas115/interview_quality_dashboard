Analyze my codebase and update my Groq service class with a dynamic fallback mechanism for multiple API keys. Here's what I need implemented:



**Requirements:**



1. **Dynamic API Key Fallback System:**

   - Create a service class that accepts a list of Groq API keys (the number should be dynamic - could be 3, 5, or any number)

   - Implement logic to try API keys in sequence (key1 → key2 → key3, etc.)

   - Each API key should track its own usage/capacity for the free tier



2. **Free Tier Capacity Tracking:**

   - Track usage for each API key's free tier limit

   - When one key's free capacity is exhausted, automatically fall back to the next available key

   - Mark exhausted keys as unavailable until they reset



3. **Scheduler Management:**

   - When ALL API keys have exhausted their free capacity, stop/deactivate the scheduler

   - Implement a per-hour scheduler that runs automatically to check if any API key has become available again (free tier reset)

   - Make the scheduler interval dynamically configurable (currently hourly, but should support half-hour or any custom interval in the future)



4. **Configuration:**

   - The scheduler interval should be easily configurable via environment variables or config file

   - Support changing from 1 hour to 30 minutes without code changes



**Please provide:**

- The updated/modified service class code

- The scheduler implementation

- Any necessary configuration files or environment variable definitions

- Clear comments explaining the fallback logic and how to add/remove API keys

- Logs everything correctly with proper professional message so I have clear picture which key is using how many attempt on that hit, Free Limit is exhausted, etc.



**Context about my setup:**

- [Specify your programming language/framework: .NET Core, C#, etc]

- [Specify where your current Groq service class is located]

- [Specify your current scheduler implementation if any exists]

- [Specify how you're currently storing API keys (environment variables, config file, database, etc.)]