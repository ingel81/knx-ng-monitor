Logging:
- im Backend Serilog und einem Console Sink verwenden.  mit akuratem Template
- Backend Logs aufräumen
- Frontend Logs aufräumen

DB:
ich sehe das da immer select gemacht werden wenn Mappings aufgelöst werden sollen
das ist enorm ineffizient. bitte das in memory in einem Service machen und nur initial bzw. bei projektwechsel refreshen aus der DB

